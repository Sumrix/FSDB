using Microsoft.Extensions.Logging;

namespace FSDB.Building;

public sealed class RecordCodecContext(ILoggerFactory loggerFactory)
{
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
}
