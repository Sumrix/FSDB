using System;
using Microsoft.Extensions.Logging;

namespace FSDB.Infrastructure.Logging;

internal sealed class TableLoggerFactory(ILoggerFactory inner, string tableName) : ILoggerFactory
{
    private readonly ILoggerFactory _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly string _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));

    public ILogger CreateLogger(string categoryName) =>
        _inner.CreateLogger(categoryName).CreateTableScopedLogger(_tableName);

    public void AddProvider(ILoggerProvider provider) => _inner.AddProvider(provider);

    // This wrapper does not own the inner factory lifecycle.
    public void Dispose()
    {
    }
}
