using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Encoding;
using FSDB.FileStorage;
using FSDB.Indexing.Scopes;
using FSDB.Indexing.State;
using FSDB.Infrastructure.Primitives;
using FSDB.Model;
using FSDB.Retry;
using FSDB.Runtime;

namespace FSDB.Indexing.Reconciliation;

public class FileReconciler<TKey, TRecord, TProjection>(
    TableContext<TKey, TRecord, TProjection> context,
    IFileStore fileStore,
    RecordStore<TKey, TRecord> recordStore,
    TableIndex<TKey, TRecord, TProjection> index)
    where TKey : notnull
    where TRecord : class, IRecord<TKey>
{
    private readonly FileReconciliationDecisionMaker<TKey, TRecord, TProjection> _decisionMaker = new(context.KeyEqualityComparer);
    private readonly FileReconciliationExecutor<TKey, TRecord, TProjection> _executor = new();

    public async Task<RetryDecision> ReconcileAsync(string path, CancellationToken ct)
    {
        using var sharedIndexScope = await index.EnterSharedScopeAsync(ct);

        var firstPass = await DecideAsync(path, sharedIndexScope, null, ct);
        if (firstPass.Decision == FileReconciliationDecision.Skip)
        {
            return firstPass.ReadResult.Error?.Persistence == FileErrorPersistence.Transient
                ? RetryDecision.RetryWithBackoff
                : RetryDecision.Complete;
        }

        var (firstPassIds, indexId, fileId) = GetRequiredIds(firstPass.IndexedState, firstPass.ReadResult);
        using var scopes = await sharedIndexScope.LockRecordsAsync(indexId, fileId, ct);

        var secondPass = await DecideAsync(path, sharedIndexScope, firstPass.ReadResult, ct);
        return ExecutePass(path, secondPass, scopes.First, scopes.Second, firstPassIds);
    }

    public RetryDecision ContinueAfterRead(
        string path,
        SharedIndexScope<TKey, TRecord, TProjection> sharedIndexScope,
        RecordScope<TKey, TRecord, TProjection> heldScope,
        FileReadResult<RecordDecodeResult<TRecord>> readResult)
    {
        var fileName = Path.GetFileName(path);
        var indexedState = sharedIndexScope.Files.GetValueOrDefault(fileName);
        var decision = _decisionMaker.MakePostReadDecision(readResult, indexedState);

        var heldIds = new HashSet<TKey>(context.KeyEqualityComparer) { heldScope.Id };
        var pass = new DecisionPass(decision, readResult.Fingerprint, readResult, indexedState);
        return ExecutePass(path, pass, heldScope, null, heldIds);
    }

    private RetryDecision ExecutePass(
        string path,
        DecisionPass pass,
        RecordScope<TKey, TRecord, TProjection>? firstScope,
        RecordScope<TKey, TRecord, TProjection>? secondScope,
        HashSet<TKey> heldIds)
    {
        if (pass.Decision == FileReconciliationDecision.Skip)
        {
            return pass.ReadResult.Error?.Persistence == FileErrorPersistence.Transient
                ? RetryDecision.RetryWithBackoff
                : RetryDecision.Complete;
        }

        var (requiredIds, indexId, fileId) = GetRequiredIds(pass.IndexedState, pass.ReadResult);
        if (!heldIds.IsSupersetOf(requiredIds))
        {
            return RetryDecision.RetryWithMinBackoff;
        }

        var indexScope = GetScope(firstScope, secondScope, indexId);
        var fileScope = GetScope(firstScope, secondScope, fileId);

        var fileName = Path.GetFileName(path);
        return _executor.Execute(
            pass.Decision,
            fileName,
            pass.Fingerprint,
            pass.ReadResult,
            indexScope,
            fileScope);
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

        var decision = _decisionMaker.MakePreReadDecision(fingerprint, indexedState);

        if (decision == FileReconciliationDecision.ReadFile)
        {
            var readResult = await ReadFile(path, readCache, fingerprint, ct);

            decision = _decisionMaker.MakePostReadDecision(readResult, indexedState);
            return new(decision, readResult.Fingerprint, readResult, indexedState);
        }
        else
        {
            return new(decision, fingerprint, default, indexedState);
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
        FileReconciliationDecision Decision,
        FileFingerprint Fingerprint,
        FileReadResult<RecordDecodeResult<TRecord>> ReadResult,
        IReadOnlyFileIndexState<TKey, TProjection>? IndexedState);
}
