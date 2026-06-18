using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Retry;

namespace FSDB.Tests.TestSupport;

internal sealed class InlineWorkScheduler : IWorkScheduler<string>
{
    private readonly Queue<(string Value, Func<string, CancellationToken, Task<ProcessResult>> Processor)> _queue = new();
    private bool _disposed;
    public int PendingCount => _queue.Count;

    public void Enqueue(string value, Func<string, CancellationToken, Task<ProcessResult>> processor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _queue.Enqueue((value, processor));
    }

    public async Task<bool> RunNextAsync(CancellationToken ct = default)
    {
        if (_queue.Count == 0)
            return false;

        var (value, processor) = _queue.Dequeue();
        var result = await processor(value, ct);
        if (result != ProcessResult.Complete)
        {
            _queue.Enqueue((value, processor));
        }

        return true;
    }

    public async Task RunAllAsync(CancellationToken ct = default)
    {
        var safety = 0;
        while (_queue.Count > 0)
        {
            await RunNextAsync(ct);
            safety++;
            if (safety > 10_000)
                throw new InvalidOperationException("InlineWorkScheduler exceeded safety iteration limit.");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _queue.Clear();
    }
}
