using Microsoft.Extensions.Logging;

namespace FSDB.Infrastructure.Logging;

internal sealed class TableLogger<T>(ILogger<T> inner, string tableName) : TableLogger(inner, tableName), ILogger<T>
{
}
