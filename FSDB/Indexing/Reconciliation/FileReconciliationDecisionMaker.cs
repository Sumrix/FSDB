using System;
using System.Collections.Generic;
using FSDB.Encoding;
using FSDB.FileStorage;
using FSDB.Indexing.State;
using FSDB.Model;

namespace FSDB.Indexing.Reconciliation;

public class FileReconciliationDecisionMaker<TKey, TRecord, TProjection>
    where TRecord : IRecord<TKey>
{
    private readonly IEqualityComparer<TKey> _keyEqualityComparer;

    public FileReconciliationDecisionMaker(IEqualityComparer<TKey> keyEqualityComparer)
    {
        ArgumentNullException.ThrowIfNull(keyEqualityComparer);
        _keyEqualityComparer = keyEqualityComparer;
    }

    public FileReconciliationDecision MakePreReadDecision(
        FileFingerprint fileFingerprint,
        IReadOnlyFileIndexState<TKey, TProjection>? indexedState)
    {
        if (!fileFingerprint.Exists)
        {
            return indexedState is null
                ? FileReconciliationDecision.Skip
                : FileReconciliationDecision.Delete;
        }

        if (indexedState is null)
        {
            return FileReconciliationDecision.ReadFile;
        }

        return indexedState.ErrorInfo is null &&
               indexedState.Fingerprint == fileFingerprint
            ? FileReconciliationDecision.Skip
            : FileReconciliationDecision.ReadFile;
    }

    public FileReconciliationDecision MakePostReadDecision(
        FileReadResult<RecordDecodeResult<TRecord>> readResult,
        IReadOnlyFileIndexState<TKey, TProjection>? indexedState)
    {
        if (!readResult.Fingerprint.Exists)
        {
            return indexedState is null
                ? FileReconciliationDecision.Skip
                : FileReconciliationDecision.Delete;
        }

        if (indexedState is null)
        {
            return readResult.Error is null
                ? FileReconciliationDecision.UpsertRecord
                : FileReconciliationDecision.Skip;
        }

        if (readResult.Error is not null)
        {
            if (indexedState.ErrorInfo is null)
            {
                return FileReconciliationDecision.UpsertError;
            }

            var fileError = readResult.Error.ToErrorInfo();
            return indexedState.Fingerprint == readResult.Fingerprint &&
                   indexedState.ErrorInfo == fileError
                ? FileReconciliationDecision.Skip
                : FileReconciliationDecision.UpsertError;
        }

        var fileId = readResult.Value.Record.Id;
        if (!_keyEqualityComparer.Equals(fileId, indexedState.Record.Id))
        {
            return FileReconciliationDecision.DeleteThenUpsertRecord;
        }

        return indexedState.ErrorInfo is not null ||
               indexedState.Fingerprint != readResult.Fingerprint
            ? FileReconciliationDecision.UpsertRecord
            : FileReconciliationDecision.Skip;
    }
}
