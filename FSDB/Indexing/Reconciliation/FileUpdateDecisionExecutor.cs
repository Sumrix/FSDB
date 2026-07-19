using System;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Encoding;
using FSDB.FileStorage;
using FSDB.Indexing.Scopes;
using FSDB.Indexing.State;

namespace FSDB.Indexing.Reconciliation;

public class FileUpdateDecisionExecutor<TKey, TRecord, TProjection>(
    RecordStore<TKey, TRecord> recordStore)
    where TKey : notnull
{
    public async Task<FileError?> ExecuteAsync(
        FileUpdateDecision decision,
        string path,
        string fileName,
        FileReadResult<RecordDecodeResult<TRecord>> readResult,
        RecordScope<TKey, TRecord, TProjection> fileRecordScope,
        CancellationToken ct)
    {
        if (decision == FileUpdateDecision.DoNothing)
        {
            return null;
        }

        if (decision != FileUpdateDecision.UpdateFile)
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, null);
        }

        var writeResult = await recordStore.WriteAsync(path, readResult.Value.Record, ct);
        if (!writeResult.IsSuccess)
        {
            return writeResult.Error;
        }

        var result = fileRecordScope.Upsert(
            fileName,
            writeResult.Fingerprint.Value,
            readResult.Value.TargetSchemaVersion,
            readResult.Value.Record);
        return result switch
        {
            IndexOperationResult.Applied => null,
            IndexOperationResult.NoChanges =>
                throw new InvalidOperationException("File update result did not apply to the index."),
            IndexOperationResult.BlockedByAnotherId =>
                throw new InvalidOperationException("File update result belongs to another id."),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
        };
    }
}