using Microsoft.Extensions.Logging;

namespace FSDB.Model.Building;

public sealed class RecordCodecContext(ILoggerFactory loggerFactory)
{
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
}
