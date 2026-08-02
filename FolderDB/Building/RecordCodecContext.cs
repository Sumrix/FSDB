using Microsoft.Extensions.Logging;

namespace FolderDB.Building;

public sealed class RecordCodecContext(ILoggerFactory loggerFactory)
{
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
}
