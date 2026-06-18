using System;

namespace FSDB.Indexing.Scopes;

public class ExclusiveIndexScope<TKey, TRecord, TProjection>(
    IRecordScopedIndexEngine<TKey, TRecord, TProjection> engine,
    IDisposable writerLock) : IDisposable
    where TKey : notnull
{
    public void Clear()
    {
        engine.Clear();
    }

    public void Dispose()
    {
        writerLock.Dispose();
    }
}
