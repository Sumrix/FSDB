namespace FSDB.Model;

public sealed class DefaultWorkSchedulerOptions
{
    public int IntervalMs { get; init; } = 100;
    public int MaxRetryIntervals { get; init; } = 10;
    public int BackoffMultiplier { get; init; } = 2;
}
