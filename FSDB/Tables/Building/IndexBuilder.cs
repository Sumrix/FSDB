using System;
using System.Threading.Tasks;
using System.Text.Json.Serialization.Metadata;
using FSDB.Index;
using FSDB.Index.Persistence;
using Microsoft.Extensions.Logging;

namespace FSDB.Tables.Building;

public abstract class IndexBuilder<TKey, TRecord, TProjection, TBuilder>
    : TableOptionsBuilder<TKey, TRecord, TBuilder>
    where TRecord : class, IRecord<TKey>
    where TKey : notnull
    where TBuilder : TableOptionsBuilder<TKey, TRecord, TBuilder>
{
    private readonly ProjectionStep<TKey, TRecord, TProjection> _step;

    internal IndexBuilder(
        ProjectionStep<TKey, TRecord, TProjection> step,
        TableOptions<TKey, TRecord> options)
        : base(options)
    {
        _step = step;
    }

    public FinalBuilder<TKey, TRecord, TProjection> UseIndexEngine(
        IRecordScopedIndexEngine<TKey, TRecord, TProjection> indexEngine)
    {
        ArgumentNullException.ThrowIfNull(indexEngine);
        return UseIndexEngine(_ => Task.FromResult(indexEngine));
    }

    public FinalBuilder<TKey, TRecord, TProjection> UseIndexEngine(
        Func<RecordScopedIndexEngineContext<TKey, TRecord, TProjection>, IRecordScopedIndexEngine<TKey, TRecord, TProjection>> indexEngineFactory)
    {
        ArgumentNullException.ThrowIfNull(indexEngineFactory);
        return UseIndexEngine(ctx => Task.FromResult(indexEngineFactory(ctx)));
    }

    public FinalBuilder<TKey, TRecord, TProjection> UseIndexEngine(
        Func<RecordScopedIndexEngineContext<TKey, TRecord, TProjection>, Task<IRecordScopedIndexEngine<TKey, TRecord, TProjection>>> indexEngineFactory)
    {
        ArgumentNullException.ThrowIfNull(indexEngineFactory);
        return new FinalBuilder<TKey, TRecord, TProjection>(
            new(_step, null, indexEngineFactory),
            Options);
    }

    public FinalBuilder<TKey, TRecord, TProjection> UseIndexPersistence(
        ITableIndexPersistence<TKey, TProjection> indexPersistence)
    {
        ArgumentNullException.ThrowIfNull(indexPersistence);
        return UseIndexPersistence(_ => indexPersistence);
    }

    public FinalBuilder<TKey, TRecord, TProjection> UseIndexPersistence(
        Func<TableIndexPersistenceContext<TKey, TRecord, TProjection>, ITableIndexPersistence<TKey, TProjection>> indexPersistenceFactory)
    {
        ArgumentNullException.ThrowIfNull(indexPersistenceFactory);
        return new FinalBuilder<TKey, TRecord, TProjection>(
            new(_step, indexPersistenceFactory, null),
            Options);
    }

    protected FinalBuilder<TKey, TRecord, TProjection> UseJsonIndexPersistenceCore(
        JsonTypeInfo<TKey> keyTypeInfo,
        JsonTypeInfo<TProjection> projectionTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(keyTypeInfo);
        ArgumentNullException.ThrowIfNull(projectionTypeInfo);

        return UseIndexPersistence(ctx => new JsonTableIndexPersistence<TKey, TProjection>(
            keyTypeInfo,
            projectionTypeInfo,
            ctx.Table.KeyEqualityComparer,
            ctx.LoggerFactory.CreateLogger<JsonTableIndexPersistence<TKey, TProjection>>()));
    }
}
