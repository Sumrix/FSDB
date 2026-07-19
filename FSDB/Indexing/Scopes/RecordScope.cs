using System;
using System.Diagnostics.CodeAnalysis;
using FSDB.FileStorage;
using FSDB.Indexing.State;

namespace FSDB.Indexing.Scopes;

public class RecordScope<TKey, TRecord, TProjection>(
    IRecordScopedIndexEngine<TKey, TRecord, TProjection> engine,
    IDisposable idLock,
    TKey id) : IDisposable
    where TKey : notnull
{
    public TKey Id { get; } = id;

    public bool TryGetState([MaybeNullWhen(false)] out IReadOnlyRecordIndexState<TKey, TProjection> entry)
    {
        return engine.Records.TryGetValue(Id, out entry);
    }

    public bool TryReserveFileName(string fileName)
    {
        return engine.TryReserveFileName(Id, fileName);
    }

    public bool CommitReservedFileName(string fileName, FileFingerprint fingerprint, TRecord record)
    {
        return engine.CommitReservedFileName(Id, fileName, fingerprint, record);
    }

    public IndexOperationResult Upsert(
        string fileName,
        FileFingerprint fingerprint,
        int? schemaVersion,
        TRecord record)
    {
        return engine.Upsert(Id, fileName, fingerprint, schemaVersion, record);
    }

    public IndexOperationResult Upsert(string fileName, FileFingerprint fingerprint, FileErrorInfo errorInfo)
    {
        return engine.Upsert(Id, fileName, fingerprint, errorInfo);
    }

    public bool Delete()
    {
        return engine.Delete(Id);
    }

    public IndexOperationResult DeleteFile(string fileName)
    {
        return engine.DeleteFile(Id, fileName);
    }

    public void Dispose()
    {
        idLock.Dispose();
    }
}
