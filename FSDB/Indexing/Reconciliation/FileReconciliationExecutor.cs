using System;
using FSDB.Encoding;
using FSDB.FileStorage;
using FSDB.Indexing.Scopes;
using FSDB.Indexing.State;
using FSDB.Model;
using FSDB.Retry;

namespace FSDB.Indexing.Reconciliation;

public class FileReconciliationExecutor<TKey, TRecord, TProjection>
    where TKey : notnull
    where TRecord : class, IRecord<TKey>
{
    public RetryDecision Execute(
        FileReconciliationDecision decision,
        string fileName,
        FileFingerprint fingerprint,
        FileReadResult<RecordDecodeResult<TRecord>> readResult,
        RecordScope<TKey, TRecord, TProjection>? indexedRecordScope,
        RecordScope<TKey, TRecord, TProjection>? fileRecordScope)
    {
        IndexOperationResult result;
        switch (decision)
        {
            case FileReconciliationDecision.Delete:
                result = indexedRecordScope!.DeleteFile(fileName);
                return result != IndexOperationResult.Applied
                    ? throw new InvalidOperationException("Delete operation did not apply successfully.")
                    : RetryDecision.Complete;

            case FileReconciliationDecision.UpsertRecord:
                result = fileRecordScope!.Upsert(fileName, fingerprint, readResult.Value.Record);
                return result switch
                {
                    IndexOperationResult.Applied => RetryDecision.Complete,
                    IndexOperationResult.NoChanges =>
                        throw new InvalidOperationException("Upsert operation did not apply successfully."),
                    IndexOperationResult.BlockedByAnotherId =>
                        indexedRecordScope != null
                            ? throw new InvalidOperationException("Upsert operation did not apply successfully.")
                            : RetryDecision.RetryWithMinBackoff,
                    _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
                };

            case FileReconciliationDecision.UpsertError:
                result = indexedRecordScope!.Upsert(fileName, fingerprint, readResult.Error!.ToErrorInfo());
                return result != IndexOperationResult.Applied
                    ? throw new InvalidOperationException("Error upsert operation did not apply successfully.")
                    : RetryDecision.Complete;

            case FileReconciliationDecision.DeleteThenUpsertRecord:
                result = indexedRecordScope!.DeleteFile(fileName);
                if (result != IndexOperationResult.Applied)
                {
                    throw new InvalidOperationException("Delete operation did not apply successfully.");
                }

                result = fileRecordScope!.Upsert(fileName, fingerprint, readResult.Value.Record);
                return result switch
                {
                    IndexOperationResult.Applied => RetryDecision.Complete,
                    IndexOperationResult.NoChanges =>
                        throw new InvalidOperationException("Upsert operation did not apply successfully."),
                    IndexOperationResult.BlockedByAnotherId => RetryDecision.RetryWithMinBackoff,
                    _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
                };

            case FileReconciliationDecision.Skip or FileReconciliationDecision.ReadFile:
                throw new ArgumentOutOfRangeException(nameof(decision), decision,
                    "Only terminal mutation decisions can be executed.");
            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision, null);
        };
    }
}
