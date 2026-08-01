using System;
using FSDB.Encoding;
using FSDB.FileStorage;
using FSDB.Indexing.Scopes;
using FSDB.Indexing.State;

namespace FSDB.Indexing.Reconciliation;

internal class IndexDecisionExecutor<TKey, TRecord, TProjection>
    where TKey : notnull
{
    public IndexDecisionExecutionResult Execute(
        IndexMutation mutation,
        string fileName,
        FileFingerprint fingerprint,
        FileReadResult<RecordDecodeResult<TRecord>> readResult,
        RecordScope<TKey, TRecord, TProjection>? scope)
    {
        IndexOperationResult result;
        switch (mutation)
        {
            case IndexMutation.None:
                return IndexDecisionExecutionResult.Applied;

            case IndexMutation.Delete:
                result = scope!.DeleteFile(fileName);
                return result != IndexOperationResult.Applied
                    ? throw new InvalidOperationException("Delete operation did not apply successfully.")
                    : IndexDecisionExecutionResult.Applied;

            case IndexMutation.UpsertRecord:
                result = scope!.Upsert(
                    fileName,
                    fingerprint,
                    readResult.Value.SourceSchemaVersion,
                    readResult.Value.Record);
                return result switch
                {
                    IndexOperationResult.Applied => IndexDecisionExecutionResult.Applied,
                    IndexOperationResult.NoChanges =>
                        throw new InvalidOperationException("Upsert operation did not apply successfully."),
                    IndexOperationResult.BlockedByAnotherId => IndexDecisionExecutionResult.IdLockMismatch,
                    _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
                };

            case IndexMutation.UpsertError:
                result = scope!.Upsert(fileName, fingerprint, readResult.Error!.ToErrorInfo());
                return result != IndexOperationResult.Applied
                    ? throw new InvalidOperationException("Error upsert operation did not apply successfully.")
                    : IndexDecisionExecutionResult.Applied;

            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }
}
