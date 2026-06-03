using System;
using System.Threading.Tasks;
using FSDB.Index;
using FSDB.Index.Persistence;

namespace FSDB.Tables.Building;

internal record IndexStep<TKey, TRecord, TProjection>
(
    ProjectionStep<TKey, TRecord, TProjection> Previous,
    Func<TableIndexPersistenceContext<TKey, TRecord, TProjection>, ITableIndexPersistence<TKey, TProjection>>? IndexPersistenceFactory,
    Func<RecordScopedIndexEngineContext<TKey, TRecord, TProjection>, Task<IRecordScopedIndexEngine<TKey, TRecord, TProjection>>>? IndexEngineFactory
)
    where TRecord : class, IRecord<TKey>
    where TKey : notnull;
