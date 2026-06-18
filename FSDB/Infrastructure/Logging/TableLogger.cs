using System;
using Microsoft.Extensions.Logging;

namespace FSDB.Infrastructure.Logging;

internal class TableLogger(ILogger inner, string tableName) : ILogger
{
    private readonly ILogger _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly string _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        if (state is MethodScope methodScope)
        {
            return _inner.BeginScope(new TableMethodScope(_tableName, methodScope.Method));
        }

        return _inner.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
