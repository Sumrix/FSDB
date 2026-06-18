using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Indexing.Scopes;
using FSDB.Indexing.State;
using FSDB.Runtime;

namespace FSDB.Indexing;

public class TableIndex<TKey, TRecord, TProjection>(
    IRecordScopedIndexEngine<TKey, TRecord, TProjection> engine,
    TableContext<TKey, TRecord, TProjection> context) : IAsyncDisposable
    where TKey : notnull
{
    public IReadOnlyDictionary<TKey, IReadOnlyRecordIndexState<TKey, TProjection>> Records => engine.Records;

    public async Task<SharedIndexScope<TKey, TRecord, TProjection>> EnterSharedScopeAsync(CancellationToken cancellationToken = default)
    {
        var readerLock = await engine.Barrier.ReaderLockAsync(cancellationToken);
        return new(engine, context, readerLock);
    }

    public async Task<ExclusiveIndexScope<TKey, TRecord, TProjection>> EnterExclusiveScopeAsync(CancellationToken cancellationToken = default)
    {
        var writerLock = await engine.Barrier.WriterLockAsync(cancellationToken);
        return new(engine, writerLock);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return engine.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await engine.DisposeAsync();
    }
}
