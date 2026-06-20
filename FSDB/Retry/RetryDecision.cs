namespace FSDB.Retry;

/// <summary>
/// Specifies how a scheduled operation should be handled after an attempt.
/// </summary>
public enum RetryDecision
{
    /// <summary>
    /// Removes the operation from the retry queue.
    /// </summary>
    Complete,

    /// <summary>
    /// Returns the operation to the retry queue and schedules the next attempt using the current backoff progression.
    /// </summary>
    RetryWithBackoff,

    /// <summary>
    /// Returns the operation to the retry queue and resets its backoff progression to the minimum delay.
    /// </summary>
    RetryWithMinBackoff
}
