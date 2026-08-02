using Microsoft.Extensions.Logging;

namespace FolderDB.FileStorage;

public sealed class FileStoreRetryContext
{
    public required IFileStore Inner { get; init; }
    public required ILoggerFactory LoggerFactory { get; init; }
}
