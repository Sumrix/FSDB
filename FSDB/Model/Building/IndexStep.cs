using System;
using System.Threading.Tasks;
using FSDB.Indexing;
using FSDB.Indexing.Persistence;

namespace FSDB.Model.Building;

internal record IndexStep<TKey, TRecord, TProjection>
(
    ProjectionStep<TKey, TRecord, TProjection> Previous,
    Func<TableIndexPersistenceContext<TKey, TRecord, TProjection>, ITableIndexPersistence<TKey, TProjection>>? IndexPersistenceFactory,
    Func<RecordScopedIndexEngineContext<TKey, TRecord, TProjection>, Task<IRecordScopedIndexEngine<TKey, TRecord, TProjection>>>? IndexEngineFactory
)
    where TRecord : class, IRecord<TKey>
    where TKey : notnull;
