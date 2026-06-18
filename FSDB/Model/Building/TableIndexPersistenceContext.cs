using FSDB.Runtime;
using Microsoft.Extensions.Logging;

namespace FSDB.Model.Building;

public sealed class TableIndexPersistenceContext<TKey, TRecord, TProjection>(
    TableContext<TKey, TRecord, TProjection> table,
    ILoggerFactory loggerFactory)
    where TRecord : class, IRecord<TKey>
    where TKey : notnull
{
    public TableContext<TKey, TRecord, TProjection> Table { get; } = table;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
}
