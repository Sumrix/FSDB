using System;
using Microsoft.Extensions.Logging;

namespace FSDB.Infrastructure.Logging;

internal static class TableLoggerFactoryExtensions
{
    public static ILoggerFactory CreateTableScopedLoggerFactory(this ILoggerFactory loggerFactory, string tableName)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        return new TableLoggerFactory(loggerFactory, tableName);
    }
}
