using System;
using System.Threading;
using System.Threading.Tasks;

namespace FolderDB.Retry;

/// <summary>
/// Schedules retryable operations for background processing.
/// </summary>
/// <typeparam name="T">
/// The operation key or payload passed to the processor.
/// </typeparam>
public interface IRetryScheduler<T> : IDisposable
{
    /// <summary>
    /// Adds a retryable operation to the scheduler.
    /// If the same operation is already pending, its schedule is preserved unless
    /// <paramref name="minBackoff"/> requests the minimum delay.
    /// </summary>
    /// <param name="value">The operation key or payload to schedule.</param>
    /// <param name="processor">
    /// The asynchronous processor that handles the operation and returns the next retry decision.
    /// </param>
    /// <param name="minBackoff">
    /// Whether an already pending operation should be moved to the minimum delay and restart its backoff progression.
    /// </param>
    public void Enqueue(
        T value,
        Func<T, CancellationToken, Task<RetryDecision>> processor,
        bool minBackoff = false);
}
