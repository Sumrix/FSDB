using Microsoft.Extensions.Logging;

namespace FSDB.Tables.Building;

public sealed class TableIndexPersistenceContext<TKey, TRecord, TProjection>(
    TableContext<TKey, TRecord, TProjection> table,
    ILoggerFactory loggerFactory)
    where TRecord : class, IRecord<TKey>
    where TKey : notnull
{
    public TableContext<TKey, TRecord, TProjection> Table { get; } = table;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
}
