namespace FSDB.Retry;

/// <summary>
/// Specifies how a scheduled work item should be handled after processing.
/// </summary>
public enum ProcessResult
{
    /// <summary>
    /// Removes the work item from the retry queue.
    /// </summary>
    Complete,

    /// <summary>
    /// Returns the work item to the retry queue and schedules the next attempt using the current backoff progression.
    /// </summary>
    RetryWithBackoff,

    /// <summary>
    /// Returns the work item to the retry queue and resets its backoff progression to the minimum delay.
    /// </summary>
    RetryWithMinBackoff
}
