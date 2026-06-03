using System;
using System.Threading;
using System.Threading.Tasks;

namespace FSDB.Scheduling;

/// <summary>
/// Schedules work items for background processing.
/// </summary>
/// <typeparam name="T">
/// The work item key or payload passed to the processor.
/// </typeparam>
public interface IWorkScheduler<T> : IDisposable
{
    /// <summary>
    /// Adds a work item to the scheduler.
    /// If the same item is already pending, the existing scheduled work is preserved.
    /// </summary>
    /// <param name="value">The work item to schedule.</param>
    /// <param name="processor">
    /// The asynchronous processor that handles the work item and returns the next scheduling decision.
    /// </param>
    public void Enqueue(T value, Func<T, CancellationToken, Task<ProcessResult>> processor);
}
