using Microsoft.Extensions.Logging;

namespace FolderDB.Infrastructure.Logging;

internal sealed class TableLogger<T>(ILogger<T> inner, string tableName) : TableLogger(inner, tableName), ILogger<T>
{
}
