using System.Threading;
using Microsoft.Extensions.Logging;

namespace FSDB.Tables.Building;

public sealed class RecordScopedIndexEngineContext<TKey, TRecord, TProjection>
    where TRecord : class, IRecord<TKey>
    where TKey : notnull
{
    public required TableContext<TKey, TRecord, TProjection> Table { get; init; }
    public required DatabaseOptions DatabaseOptions { get; init; }
    public required string IndexFilePath { get; init; }
    public required CancellationToken CancellationToken { get; init; }

    public ILoggerFactory LoggerFactory => DatabaseOptions.LoggerFactory;
}
