using Microsoft.Extensions.Logging;

namespace FSDB.Tables.Building;

public sealed class RecordCodecContext(ILoggerFactory loggerFactory)
{
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
}
