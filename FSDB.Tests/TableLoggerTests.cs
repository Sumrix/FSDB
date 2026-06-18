using System;
using System.Collections.Generic;
using FSDB.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace FSDB.Tests;

public class TableLoggerTests
{
    [Fact]
    public void BeginMethodScope_WhenLoggerCreatedViaGenericFactory_WrapsMethodScopeIntoTableMethodScope()
    {
        var innerFactory = new CaptureLoggerFactory();
        var loggerFactory = innerFactory.CreateTableScopedLoggerFactory("users");
        var logger = loggerFactory.CreateLogger<TableLoggerTests>();

        using var _ = logger.BeginMethodScope();

        var scope = Assert.IsType<TableMethodScope>(innerFactory.Logger.LastScope);

        Assert.Equal(2, scope.Count);
        Assert.Equal(new KeyValuePair<string, object?>("Table", "users"), scope[0]);
        Assert.Equal(new KeyValuePair<string, object?>("Method", nameof(BeginMethodScope_WhenLoggerCreatedViaGenericFactory_WrapsMethodScopeIntoTableMethodScope)), scope[1]);
    }

    private sealed class CaptureLoggerFactory : ILoggerFactory
    {
        public CaptureLogger Logger { get; } = new();

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => Logger;

        public void Dispose()
        {
        }
    }

    private sealed class CaptureLogger : ILogger
    {
        public object? LastScope { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            LastScope = state;
            return NoopDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
