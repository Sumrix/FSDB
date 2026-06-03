using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace FSDB.Logging;

internal static class TableLoggerExtensions
{
    public static ILogger CreateTableScopedLogger(this ILogger logger, string tableName)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return new TableLogger(logger, tableName);
    }

    public static ILogger<T> CreateTableScopedLogger<T>(this ILogger<T> logger, string tableName)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return new TableLogger<T>(logger, tableName);
    }

    public static IDisposable? BeginMethodScope(this ILogger logger, [CallerMemberName] string method = "")
    {
        ArgumentNullException.ThrowIfNull(logger);

        return logger.BeginScope(new MethodScope(method));
    }
}
