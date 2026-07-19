using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Encoding;
using FSDB.FileStorage;
using FSDB.Indexing.Scopes;
using FSDB.Indexing.State;
using FSDB.Infrastructure.Helpers;
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
        context.RecordCodec.CurrentSchemaVersion is int currentSchemaVersion
            ? new(currentSchemaVersion)
            : null;
    private readonly IndexDecisionExecutor<TKey, TRecord, TProjection> _indexDecisionExecutor = new();
    private readonly FileUpdateDecisionExecutor<TKey, TRecord, TProjection> _fileUpdateDecisionExecutor = new(recordStore);
    private readonly RetryDecisionMaker _retryDecisionMaker = new();
    private readonly ILogger<FileReconciler<TKey, TRecord, TProjection>> _logger =
        logger ?? NullLogger<FileReconciler<TKey, TRecord, TProjection>>.Instance;

    public async Task<RetryDecision> ReconcileAsync(string path, CancellationToken ct)
    {
        using var sharedIndexScope = await index.EnterSharedScopeAsync(ct);

        var firstPass = await DecideAsync(path, sharedIndexScope, null, ct);
        if (firstPass.IsComplete)
        {
            return _retryDecisionMaker.MakeDecision(firstPass.ReadResult.Error, idLockMismatch: false);
        }

        var (firstPassIds, indexId, fileId) = GetRequiredIds(firstPass.IndexedState, firstPass.ReadResult);
        using var scopes = await sharedIndexScope.LockRecordsAsync(indexId, fileId, ct);

        var secondPass = await DecideAsync(path, sharedIndexScope, firstPass.ReadResult, ct);
        return await ExecutePassAsync(
            path,
            secondPass,
            scopes.First,
            scopes.Second,
            firstPassIds,
            executeFileUpdateDecision: true,
            ct);
    }

    public Task<RetryDecision> ContinueAfterReadAsync(
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

        var heldIds = new HashSet<TKey>(context.KeyEqualityComparer) { heldScope.Id };
        return ExecutePassAsync(
            path,
            pass,
            heldScope,
            null,
            heldIds,
            executeFileUpdateDecision: false,
            ct);
    }

    private async Task<RetryDecision> ExecutePassAsync(
        string path,
        DecisionPass pass,
        RecordScope<TKey, TRecord, TProjection>? firstScope,
        RecordScope<TKey, TRecord, TProjection>? secondScope,
        HashSet<TKey> heldIds,
        bool executeFileUpdateDecision,
        CancellationToken ct)
    {
        if (pass.IsComplete)
        {
            return _retryDecisionMaker.MakeDecision(pass.ReadResult.Error, idLockMismatch: false);
        }

        var (requiredIds, indexId, fileId) = GetRequiredIds(pass.IndexedState, pass.ReadResult);
        if (!heldIds.IsSupersetOf(requiredIds))
        {
            return _retryDecisionMaker.MakeDecision(pass.ReadResult.Error, idLockMismatch: true);
        }

        var indexScope = GetScope(firstScope, secondScope, indexId);
        var fileScope = GetScope(firstScope, secondScope, fileId);

        var fileName = Path.GetFileName(path);
        var executionResult = _indexDecisionExecutor.ExecuteIndexDecision(
            pass.IndexDecision,
            fileName,
            pass.Fingerprint,
            pass.ReadResult,
            indexScope,
            fileScope);

        if (executionResult == IndexDecisionExecutionResult.IdLockMismatch)
        {
            return _retryDecisionMaker.MakeDecision(pass.ReadResult.Error, idLockMismatch: true);
        }

        switch (pass.FileUpdateIntent)
        {
            case null or FileUpdateIntent.DoNothing:
                return _retryDecisionMaker.MakeDecision(pass.ReadResult.Error, idLockMismatch: false);
            case FileUpdateIntent.ReadFile:
                throw new InvalidOperationException("A file update read intent cannot reach execution.");
        }

        var currentFile = fileScope!.TryGetState(out var recordState) &&
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
            fileScope,
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

        if (indexDecision == FileReconciliationDecision.ReadFile ||
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
    
    private (HashSet<TKey>, Option<TKey>, Option<TKey>) GetRequiredIds(
        IReadOnlyFileIndexState<TKey, TProjection>? indexedState,
        FileReadResult<RecordDecodeResult<TRecord>> readResult)
    {
        Option<TKey> fileId;
        Option<TKey> indexId;
        var requiredIdLocks = new HashSet<TKey>(context.KeyEqualityComparer);

        if (indexedState != null)
        {
            indexId = Option<TKey>.Some(indexedState.Record.Id);
            requiredIdLocks.Add(indexedState.Record.Id);
        }
        else
        {
            indexId = Option<TKey>.None;
        }

        if (readResult.IsSuccess)
        {
            fileId = Option<TKey>.Some(readResult.Value.Record.Id);
            requiredIdLocks.Add(readResult.Value.Record.Id);
        }
        else
        {
            fileId = Option<TKey>.None;
        }

        return (requiredIdLocks, indexId, fileId);
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

        return firstScope != null && context.KeyEqualityComparer.Equals(firstScope.Id, id.Value)
            ? firstScope
            : secondScope!;
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
