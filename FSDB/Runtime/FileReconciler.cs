using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Encoding;
using FSDB.FileStorage;
using FSDB.Indexing;
using FSDB.Indexing.Scopes;
using FSDB.Indexing.State;
using FSDB.Infrastructure.Helpers;
using FSDB.Infrastructure.Logging;
using FSDB.Infrastructure.Primitives;
using FSDB.Model;
using FSDB.Retry;
using Microsoft.Extensions.Logging;

namespace FSDB.Runtime;

internal sealed class FileReconciler<TKey, TRecord, TProjection>(
    TableContext<TKey, TRecord, TProjection> context,
    IFileStore fileStore,
    RecordStore<TKey, TRecord> store,
    TableIndex<TKey, TRecord, TProjection> index,
    ILogger logger)
    where TRecord : class, IRecord<TKey>
    where TKey : notnull
{
    public async Task<RetryDecision> ReconcileAsync(string path, CancellationToken ct)
    {
        using var sharedIndexScope = await index.EnterSharedScopeAsync(ct);
        using var _ = logger.BeginMethodScope();
        var stopwatch = Stopwatch.StartNew();

        var state = new ReconciliationState
        {
            Path = path,
            FileName = Path.GetFileName(path),
            SharedIndexScope = sharedIndexScope,
            Fingerprint = default
        };

        logger.LogTrace("Reconciling file: file=\"{FileName}\"", state.FileName);
        try
        {
            var status = await ReconcileCoreAsync(state, ct);
            var retryDecision = GetRetryDecision(status);
            logger.LogDebug(
                "Reconciliation finished: file=\"{File}\" indexId={IndexId} diskId={DiskId} fingerprint=\"{Fingerprint}\" status={Status} retryDecision={RetryDecision} durationMs={DurationMs}",
                state.FileName,
                state.IndexId,
                state.DiskId,
                FormatFingerprint(state.Fingerprint),
                status,
                retryDecision,
                stopwatch.ElapsedMilliseconds);
            return retryDecision;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Reconciliation failed, skipped: file=\"{File}\" indexId={IndexId} diskId={DiskId} fingerprint=\"{Fingerprint}\" durationMs={DurationMs}",
                state.FileName,
                state.IndexId,
                state.DiskId,
                FormatFingerprint(state.Fingerprint),
                stopwatch.ElapsedMilliseconds);
            return RetryDecision.Complete;
        }
    }

    private async Task<StopStatus> ReconcileCoreAsync(ReconciliationState state, CancellationToken ct)
    {
        using var _ = logger.BeginMethodScope();

        UpdateStateFromFileState(state);

        var stopStatus = UpdateStateFromIndex(state);
        if (stopStatus is not null) return stopStatus.Value;

        stopStatus = await UpdateStateFromFileContentAsync(state, ct);
        if (stopStatus is not null) return stopStatus.Value;

        logger.LogTrace(
            "Acquiring record locks for reconciliation: file=\"{File}\" indexId={IndexId} diskId={DiskId}",
            state.FileName,
            state.IndexId,
            state.DiskId);
        using var lockSet = await state.SharedIndexScope.LockRecordsAsync(state.IndexIdOption, state.DiskIdOption, ct);

        stopStatus = RevalidateStateFromFileState(state);
        if (stopStatus is not null) return stopStatus.Value;

        stopStatus = await CommitToIndexAsync(state, lockSet.First, lockSet.Second, ct);

        return stopStatus.Value;
    }

    private void UpdateStateFromFileState(ReconciliationState state)
    {
        state.Fingerprint = fileStore.GetFileFingerprint(state.Path);
    }

    private StopStatus? UpdateStateFromIndex(ReconciliationState state)
    {
        using var _ = logger.BeginMethodScope();

        var indexedFileState = state.SharedIndexScope.Files.GetValueOrDefault(state.FileName);
        state.IndexedFileState = indexedFileState;
        state.IndexIdOption = indexedFileState is not null
            ? Option.Some(indexedFileState.Record.Id)
            : Option.None<TKey>();

        if (indexedFileState is null)
        {
            if (!state.Fingerprint.Exists)
            {
                logger.LogTrace("File is missing and not indexed, skipping: file=\"{File}\"", state.FileName);
                return StopStatus.FileMissingAndNotIndexed;
            }
        }
        else if (indexedFileState.Status == FileIndexStatus.Committed &&
                 indexedFileState.ErrorInfo == null &&
                 indexedFileState.Fingerprint == state.Fingerprint)
        {
            state.DiskIdOption = state.IndexIdOption;
            logger.LogTrace(
                "File matches indexed state, skipping: file=\"{File}\" indexId={IndexId} fingerprint=\"{Fingerprint}\"",
                state.FileName,
                state.IndexId,
                FormatFingerprint(indexedFileState.Fingerprint));
            return StopStatus.FileInSync;
        }

        logger.LogTrace(
            "File differs from indexed state, reading content: file=\"{File}\" indexId={IndexId} fingerprint=\"{Fingerprint}\" indexFingerprint=\"{IndexFingerprint}\"",
            state.FileName,
            state.IndexId,
            FormatFingerprint(state.Fingerprint),
            FormatFingerprint(indexedFileState?.Fingerprint));
        return null;
    }

    private async Task<StopStatus?> UpdateStateFromFileContentAsync(ReconciliationState state, CancellationToken ct)
    {
        using var _ = logger.BeginMethodScope();

        if (!state.Fingerprint.Exists)
        {
            return null;
        }

        var readResult = await store.ReadAsync(state.Path, ct);
        if (readResult.Error?.Reason == FileErrorReason.Unavailable)
        {
            if (readResult.Error.Persistence == FileErrorPersistence.Transient)
            {
                logger.LogTrace(
                    "File content read interrupted by transient failure: file=\"{File}\" indexId={IndexId}",
                    state.FileName,
                    state.IndexId);

                return StopStatus.FileAccessTransientFailure;
            }
            else
            {
                state.ErrorInfo = readResult.Error.ToErrorInfo();

                if (IsIndexedErrorInSync(state))
                    return StopStatus.FileInSync;

                logger.LogTrace(
                    "File content read failed, marking unavailable: file=\"{File}\" indexId={IndexId}",
                    state.FileName,
                    state.IndexId);

                return state.IndexIdOption.HasValue
                    ? null
                    : StopStatus.FileReadFailedAndNotIndexed;
            }
        }

        state.Fingerprint = readResult.Fingerprint;

        if (!state.Fingerprint.Exists)
        {
            logger.LogTrace(
                "File is missing after content read: file=\"{File}\" indexId={IndexId}",
                state.FileName,
                state.IndexId);

            return state.IndexIdOption.HasValue
                ? null
                : StopStatus.FileMissingAndNotIndexed;
        }

        if (readResult.Error?.Reason == FileErrorReason.Invalid)
        {
            state.ErrorInfo = readResult.Error.ToErrorInfo();

            if (IsIndexedErrorInSync(state))
                return StopStatus.FileInSync;

            logger.LogTrace(
                "File is invalid, marking invalid: file=\"{File}\" indexId={IndexId} fingerprint=\"{Fingerprint}\"",
                state.FileName,
                state.IndexId,
                FormatFingerprint(state.Fingerprint));

            return state.IndexIdOption.HasValue
                ? null
                : StopStatus.InvalidFileNotIndexed;
        }

        var decodeResult = readResult.Value!;
        state.DiskIdOption = Option.Some(decodeResult.Record.Id);
        state.DecodeResult = decodeResult;
        state.SchemaVersion = decodeResult.SourceSchemaVersion;
        logger.LogTrace(
            "File content loaded from disk: file=\"{File}\" diskId={DiskId} fingerprint=\"{Fingerprint}\" schemaConverted={SchemaConverted} fromVersion={FromVersion} toVersion={ToVersion}",
            state.FileName,
            state.DiskId,
            FormatFingerprint(state.Fingerprint),
            decodeResult.Upgraded,
            decodeResult.SourceSchemaVersion,
            decodeResult.TargetSchemaVersion);

        return null;
    }

    private bool IsIndexedErrorInSync(ReconciliationState state)
    {
        if (state.IndexedFileState?.Status != FileIndexStatus.Committed ||
            state.IndexedFileState.Fingerprint != state.Fingerprint ||
            state.IndexedFileState.ErrorInfo != state.ErrorInfo)
        {
            return false;
        }

        logger.LogTrace(
            "File error matches indexed state, skipping: file=\"{File}\" indexId={IndexId} fingerprint=\"{Fingerprint}\" errorReason={ErrorReason} errorPersistence={ErrorPersistence}",
            state.FileName,
            state.IndexId,
            FormatFingerprint(state.Fingerprint),
            state.ErrorInfo?.Reason,
            state.ErrorInfo?.Persistence);
        return true;
    }

    private StopStatus? RevalidateStateFromFileState(ReconciliationState state)
    {
        var fingerprint = fileStore.GetFileFingerprint(state.Path);
        return state.Fingerprint.Equals(fingerprint)
            ? null
            : StopStatus.FileChangedDuringReconciliation;
    }

    private async Task<StopStatus> CommitToIndexAsync(
        ReconciliationState state,
        RecordScope<TKey, TRecord, TProjection>? indexRecordScope,
        RecordScope<TKey, TRecord, TProjection>? diskRecordScope,
        CancellationToken ct)
    {
        var newFileState = state.SharedIndexScope.Files.GetValueOrDefault(state.FileName);
        var newIndexId = newFileState is not null
            ? Option.Some(newFileState.Record.Id)
            : Option.None<TKey>();
        var status = StopStatus.NoChanges;
        
        if (newIndexId.Equals(state.DiskIdOption, context.KeyEqualityComparer))
        {
            if (state.DiskIdOption.HasValue)
            {
                await TryPersistUpgradedRecordAsync(state, diskRecordScope!, state.DecodeResult!.Value, ct);
                var result = diskRecordScope!.Upsert(
                    state.FileName,
                    state.Fingerprint,
                    state.SchemaVersion,
                    state.DecodeResult!.Value.Record);
                
                status = result == IndexOperationResult.Applied
                    ? StopStatus.UpdatedIndex
                    : StopStatus.FileInSync;
            }
        }
        else if (newIndexId.Equals(state.IndexIdOption, context.KeyEqualityComparer))
        {
            if (state.IndexIdOption.HasValue)
            {
                if (state.ErrorInfo != null)
                {
                    var result = indexRecordScope!.Upsert(state.FileName, state.Fingerprint, state.ErrorInfo!);

                    status = result == IndexOperationResult.Applied
                        ? state.ErrorInfo.Reason == FileErrorReason.Unavailable
                            ? StopStatus.MarkedFileUnavailable
                            : StopStatus.MarkedFileInvalid
                        : StopStatus.FileInSync;
                }
                else
                {
                    status = indexRecordScope!.DeleteFile(state.FileName) == IndexOperationResult.Applied
                        ? StopStatus.RemovedFileFromIndex
                        : StopStatus.FileInSync;
                }
            }

            if (state.DiskIdOption.HasValue)
            {
                await TryPersistUpgradedRecordAsync(state, diskRecordScope!, state.DecodeResult!.Value, ct);
                var result = diskRecordScope!.Upsert(
                    state.FileName,
                    state.Fingerprint,
                    state.SchemaVersion,
                    state.DecodeResult!.Value.Record);

                status = result == IndexOperationResult.Applied
                    ? state.IndexIdOption.HasValue
                        ? StopStatus.OwnershipChanged
                        : StopStatus.AddedFileToIndex
                    : status;
            }
        }
        else
        {
            // The new index id is different from the old index id and disk id.
            // That means we didn't lock the new index id, and we can't touch the index.
            return StopStatus.IndexChangedDuringReconciliation;
        }

        return status;
    }

    private async Task TryPersistUpgradedRecordAsync(
        ReconciliationState state,
        RecordScope<TKey, TRecord, TProjection> recordScope,
        RecordDecodeResult<TRecord> decodeResult,
        CancellationToken ct)
    {
        if (!decodeResult.Upgraded || (recordScope.TryGetState(out var recordState) &&
                                       !PathHelper.OSDependedPathComparer.Equals(recordState.CurrentFileName, state.FileName)))
        {
            return;
        }

        var writeResult = await store.WriteAsync(state.Path, decodeResult.Record, ct);
        if (writeResult.IsSuccess)
        {
            state.Fingerprint = writeResult.Fingerprint.Value;
            state.SchemaVersion = decodeResult.TargetSchemaVersion;
            logger.LogTrace(
                "Upgraded record persisted to disk: file=\"{File}\" diskId={DiskId} fingerprint=\"{Fingerprint}\" fromVersion={FromVersion} toVersion={ToVersion}",
                state.FileName,
                state.DiskId,
                FormatFingerprint(state.Fingerprint),
                decodeResult.SourceSchemaVersion,
                decodeResult.TargetSchemaVersion);
        }
        else
        {
            logger.LogWarning(
                "Upgraded record write deferred: file=\"{File}\" diskId={DiskId} fromVersion={FromVersion} toVersion={ToVersion} errorReason={ErrorReason} errorPersistence={ErrorPersistence}",
                state.FileName,
                state.DiskId,
                decodeResult.SourceSchemaVersion,
                decodeResult.TargetSchemaVersion,
                GetErrorReason(writeResult),
                GetErrorPersistence(writeResult));
        }
    }

    private static object? GetLogValue(Option<TKey> value) => value.HasValue ? value.Value : null;

    private static RetryDecision GetRetryDecision(StopStatus status)
    {
        return status switch
        {
            StopStatus.FileAccessTransientFailure => RetryDecision.RetryWithBackoff,
            StopStatus.FileChangedDuringReconciliation or
                StopStatus.IndexChangedDuringReconciliation => RetryDecision.RetryWithMinBackoff,
            _ => RetryDecision.Complete
        };
    }

    private static string FormatFingerprint(FileFingerprint fingerprint) => fingerprint.ToString();

    private static string FormatFingerprint(FileFingerprint? fingerprint) => fingerprint?.ToString() ?? "null";
    
    private static FileErrorReason? GetErrorReason(FileWriteResult result)
    {
        return result.Error?.Reason;
    }

    private static FileErrorPersistence? GetErrorPersistence(FileWriteResult result)
    {
        return result.Error?.Persistence;
    }

    private class ReconciliationState
    {
        public required string Path { get; init; }
        public required SharedIndexScope<TKey, TRecord, TProjection> SharedIndexScope { get; init; }
        public required string FileName { get; init; }
        public object? IndexId => GetLogValue(IndexIdOption);
        public object? DiskId => GetLogValue(DiskIdOption);
        public required FileFingerprint Fingerprint { get; set; }
        public Option<TKey> IndexIdOption { get; set; }
        public Option<TKey> DiskIdOption { get; set; }
        public IReadOnlyFileIndexState<TKey, TProjection>? IndexedFileState { get; set; }
        public RecordDecodeResult<TRecord>? DecodeResult { get; set; }
        public int? SchemaVersion { get; set; }
        public FileErrorInfo? ErrorInfo { get; set; }

    }

    private enum StopStatus
    {
        NoChanges,
        FileInSync,
        FileMissingAndNotIndexed,
        InvalidFileNotIndexed,
        FileReadFailedAndNotIndexed,
        FileAccessTransientFailure,
        FileChangedDuringReconciliation,
        IndexChangedDuringReconciliation,
        AddedFileToIndex,
        UpdatedIndex,
        OwnershipChanged,
        MarkedFileInvalid,
        MarkedFileUnavailable,
        RemovedFileFromIndex
    }
}
