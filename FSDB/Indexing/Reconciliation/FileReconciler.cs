using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Encoding;
using FSDB.FileStorage;
using FSDB.Indexing.Scopes;
using FSDB.Indexing.State;
using FSDB.Infrastructure.Helpers;
using FSDB.Infrastructure.Logging;
using FSDB.Infrastructure.Primitives;
using FSDB.Model;
using FSDB.Retry;
using FSDB.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSDB.Indexing.Reconciliation;

public class FileReconciler<TKey, TRecord, TProjection>(
    TableContext<TKey, TRecord, TProjection> context,
    IFileStore fileStore,
    RecordStore<TKey, TRecord> recordStore,
    TableIndex<TKey, TRecord, TProjection> index,
    ILogger<FileReconciler<TKey, TRecord, TProjection>>? logger = null)
    where TKey : notnull
    where TRecord : class, IRecord<TKey>
{
    private readonly IndexDecisionMaker<TKey, TRecord, TProjection> _indexDecisionMaker = new(context.KeyEqualityComparer);
    private readonly FileUpdateDecisionMaker<TKey, TRecord, TProjection>? _fileUpdateDecisionMaker =
        context.RecordCodec.CurrentSchemaVersion is { } currentSchemaVersion
            ? new(currentSchemaVersion)
            : null;
    private static readonly IndexDecisionExecutor<TKey, TRecord, TProjection> _indexDecisionExecutor = new();
    private readonly FileUpdateDecisionExecutor<TKey, TRecord, TProjection> _fileUpdateDecisionExecutor = new(recordStore);
    private static readonly RetryDecisionMaker _retryDecisionMaker = new();
    private readonly ILogger<FileReconciler<TKey, TRecord, TProjection>> _logger =
        logger ?? NullLogger<FileReconciler<TKey, TRecord, TProjection>>.Instance;

    public async Task<RetryDecision> ReconcileAsync(string path, CancellationToken ct)
    {
        using var _ = _logger.BeginMethodScope();
        var stopwatch = Stopwatch.StartNew();
        var fileName = Path.GetFileName(path);

        try
        {
            var decision = await ReconcileCoreAsync(path, ct);

            _logger.LogDebug(
                "File reconciliation finished: file=\"{File}\" retryDecision={RetryDecision} durationMs={DurationMs}",
                fileName,
                decision,
                stopwatch.ElapsedMilliseconds);

            return decision;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "File reconciliation failed, will retry: file=\"{File}\" durationMs={DurationMs}",
                fileName,
                stopwatch.ElapsedMilliseconds);

            return RetryDecision.RetryWithBackoff;
        }
    }

    public async Task<RetryDecision> ContinueAfterReadAsync(
        string path,
        SharedIndexScope<TKey, TRecord, TProjection> sharedIndexScope,
        RecordScope<TKey, TRecord, TProjection> heldScope,
        FileReadResult<RecordDecodeResult<TRecord>> readResult,
        CancellationToken ct = default)
    {
        using var _ = _logger.BeginMethodScope();
        var fileName = Path.GetFileName(path);

        try
        {
            var decision = await ContinueAfterReadCoreAsync(path, sharedIndexScope, heldScope, readResult, ct);

            _logger.LogDebug(
                "Partial file reconciliation finished: file=\"{File}\" retryDecision={RetryDecision}",
                fileName,
                decision);

            return decision;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Partial file reconciliation failed, will retry: file=\"{File}\"",
                fileName);

            return RetryDecision.RetryWithBackoff;
        }
    }

    private async Task<RetryDecision> ReconcileCoreAsync(string path, CancellationToken ct)
    {
        using var sharedIndexScope = await index.EnterSharedScopeAsync(ct);

        var firstPass = await DecideAsync(path, sharedIndexScope, null, ct);
        if (firstPass.IsComplete)
        {
            return _retryDecisionMaker.MakeDecision(firstPass.ReadResult.Error, idLockMismatch: false);
        }

        var (indexedId, diskId) = GetRequiredIdLocks(firstPass.IndexedState, firstPass.ReadResult);
        using var scopes = await sharedIndexScope.LockRecordsAsync(indexedId, diskId, ct);

        var secondPass = await DecideAsync(path, sharedIndexScope, firstPass.ReadResult, ct);
        return await ExecutePassAsync(
            path,
            secondPass,
            scopes.First,
            scopes.Second,
            executeFileUpdateDecision: true,
            ct);
    }

    private Task<RetryDecision> ContinueAfterReadCoreAsync(
        string path,
        SharedIndexScope<TKey, TRecord, TProjection> sharedIndexScope,
        RecordScope<TKey, TRecord, TProjection> heldScope,
        FileReadResult<RecordDecodeResult<TRecord>> readResult,
        CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(path);
        var indexedState = sharedIndexScope.Files.GetValueOrDefault(fileName);
        var indexDecision = _indexDecisionMaker.MakePostReadDecision(readResult, indexedState);
        var fileUpdateIntent = _fileUpdateDecisionMaker?.MakePostReadIntent(readResult);
        var pass = new DecisionPass(
            indexDecision,
            fileUpdateIntent,
            readResult.Fingerprint,
            readResult,
            indexedState);

        return ExecutePassAsync(
            path,
            pass,
            heldScope,
            null,
            executeFileUpdateDecision: false,
            ct);
    }

    private async Task<RetryDecision> ExecutePassAsync(
        string path,
        DecisionPass pass,
        RecordScope<TKey, TRecord, TProjection>? firstScope,
        RecordScope<TKey, TRecord, TProjection>? secondScope,
        bool executeFileUpdateDecision,
        CancellationToken ct)
    {
        if (pass.IsComplete)
        {
            return _retryDecisionMaker.MakeDecision(pass.ReadResult.Error, idLockMismatch: false);
        }

        if (pass.IndexDecision.RequiresRead)
        {
            throw new InvalidOperationException("A read decision cannot reach execution.");
        }

        if (pass.FileUpdateIntent == FileUpdateIntent.ReadFile)
        {
            throw new InvalidOperationException("A file update read intent cannot reach execution.");
        }

        var (indexedId, diskId) = GetRequiredIdLocks(pass.IndexedState, pass.ReadResult);
        var indexedIdScope = GetScope(firstScope, secondScope, indexedId);
        var diskIdScope = GetScope(firstScope, secondScope, diskId);
        var fileName = Path.GetFileName(path);

        // Execute the indexed id part of the index reconciliation decision
        var indexedIdPart = pass.IndexDecision.IndexedIdPart;
        if (indexedIdPart != IndexMutation.None && indexedIdScope is null)
        {
            return _retryDecisionMaker.MakeDecision(pass.ReadResult.Error, idLockMismatch: true);
        }

        _indexDecisionExecutor.Execute(
            indexedIdPart,
            fileName,
            pass.Fingerprint,
            pass.ReadResult,
            indexedIdScope);

        // Execute the disk id part of the index reconciliation decision and the FileUpdateIntent
        var diskIdPart = pass.IndexDecision.DiskIdPart;
        var diskIdLockRequired =
            diskIdPart != IndexMutation.None ||
            pass.FileUpdateIntent == FileUpdateIntent.UpdateIfCurrentFile;
        if (diskIdLockRequired && diskIdScope is null)
        {
            return _retryDecisionMaker.MakeDecision(pass.ReadResult.Error, idLockMismatch: true);
        }

        var diskIdResult = _indexDecisionExecutor.Execute(
            diskIdPart,
            fileName,
            pass.Fingerprint,
            pass.ReadResult,
            diskIdScope);
        if (diskIdResult == IndexDecisionExecutionResult.IdLockMismatch)
        {
            return _retryDecisionMaker.MakeDecision(pass.ReadResult.Error, idLockMismatch: true);
        }

        // Resolve FileUpdateIntent against the recalculated CurrentFileName and update the file
        if (pass.FileUpdateIntent is null or FileUpdateIntent.DoNothing)
        {
            return _retryDecisionMaker.MakeDecision(pass.ReadResult.Error, idLockMismatch: false);
        }

        var currentFile = diskIdScope!.TryGetState(out var recordState) &&
                          PathHelper.OSDependedPathComparer.Equals(recordState.CurrentFileName, fileName);
        var fileUpdateDecision = _fileUpdateDecisionMaker!.MakeDecision(pass.FileUpdateIntent.Value, currentFile);
        if (fileUpdateDecision == FileUpdateDecision.DoNothing)
        {
            return _retryDecisionMaker.MakeDecision(pass.ReadResult.Error, idLockMismatch: false);
        }

        if (!executeFileUpdateDecision)
        {
            return RetryDecision.RetryWithMinBackoff;
        }

        var writeError = await _fileUpdateDecisionExecutor.ExecuteAsync(
            fileUpdateDecision,
            path,
            fileName,
            pass.ReadResult,
            diskIdScope,
            ct);

        if (writeError is not null)
        {
            _logger.LogWarning(
                "File format update failed: file=\"{File}\" id={Id} fromSchemaVersion={FromSchemaVersion} toSchemaVersion={ToSchemaVersion} errorReason={ErrorReason} errorPersistence={ErrorPersistence}",
                fileName,
                pass.ReadResult.Value.Record.Id,
                pass.ReadResult.Value.SourceSchemaVersion,
                pass.ReadResult.Value.TargetSchemaVersion,
                writeError.Reason,
                writeError.Persistence);
        }

        return _retryDecisionMaker.MakeDecision(writeError, idLockMismatch: false);
    }

    private async Task<DecisionPass> DecideAsync(
        string path,
        SharedIndexScope<TKey, TRecord, TProjection> sharedIndexScope,
        FileReadResult<RecordDecodeResult<TRecord>>? readCache,
        CancellationToken ct)
    {
        var fileName = Path.GetFileName(path);
        var fingerprint = fileStore.GetFileFingerprint(path);
        var indexedState = sharedIndexScope.Files.GetValueOrDefault(fileName);

        var indexDecision = _indexDecisionMaker.MakePreReadDecision(fingerprint, indexedState);
        var fileUpdateIntent = _fileUpdateDecisionMaker?.MakePreReadIntent(fileName, fingerprint, indexedState);

        if (indexDecision.RequiresRead ||
            fileUpdateIntent == FileUpdateIntent.ReadFile)
        {
            var readResult = await ReadFile(path, readCache, fingerprint, ct);

            indexDecision = _indexDecisionMaker.MakePostReadDecision(readResult, indexedState);
            fileUpdateIntent = _fileUpdateDecisionMaker?.MakePostReadIntent(readResult);
            return new(indexDecision, fileUpdateIntent, readResult.Fingerprint, readResult, indexedState);
        }
        else
        {
            return new(indexDecision, fileUpdateIntent, fingerprint, readCache ?? default, indexedState);
        }
    }

    private async Task<FileReadResult<RecordDecodeResult<TRecord>>> ReadFile(
        string path,
        FileReadResult<RecordDecodeResult<TRecord>>? readCache,
        FileFingerprint fingerprint,
        CancellationToken ct)
    {
        return readCache is not null &&
               readCache.Value.Fingerprint == fingerprint
            ? readCache.Value
            : await recordStore.ReadAsync(path, ct);
    }
    
    private static (Option<TKey> IndexedId, Option<TKey> DiskId) GetRequiredIdLocks(
        IReadOnlyFileIndexState<TKey, TProjection>? indexedState,
        FileReadResult<RecordDecodeResult<TRecord>> readResult)
    {
        var indexedId = indexedState != null
            ? Option<TKey>.Some(indexedState.Record.Id)
            : Option<TKey>.None;

        var diskId = readResult.IsSuccess
            ? Option<TKey>.Some(readResult.Value.Record.Id)
            : Option<TKey>.None;

        return (indexedId, diskId);
    }

    private RecordScope<TKey, TRecord, TProjection>? GetScope(
        RecordScope<TKey, TRecord, TProjection>? firstScope,
        RecordScope<TKey, TRecord, TProjection>? secondScope,
        Option<TKey> id)
    {
        if (id.IsNone)
        {
            return null;
        }

        if (firstScope != null && context.KeyEqualityComparer.Equals(firstScope.Id, id.Value))
        {
            return firstScope;
        }

        return secondScope != null && context.KeyEqualityComparer.Equals(secondScope.Id, id.Value)
            ? secondScope
            : null;
    }

    private readonly record struct DecisionPass(
        FileReconciliationDecision IndexDecision,
        FileUpdateIntent? FileUpdateIntent,
        FileFingerprint Fingerprint,
        FileReadResult<RecordDecodeResult<TRecord>> ReadResult,
        IReadOnlyFileIndexState<TKey, TProjection>? IndexedState)
    {
        public bool IsComplete =>
            IndexDecision == FileReconciliationDecision.Skip &&
            FileUpdateIntent is null or FSDB.Indexing.Reconciliation.FileUpdateIntent.DoNothing;
    }
}
