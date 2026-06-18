using System;
using FSDB.Infrastructure.Helpers;

namespace FSDB.FileStorage;

public sealed class RetryFileStoreOperationOptions
{
    public int MaxAttempts { get; set; } = 1;
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    public double BackoffMultiplier { get; set; } = 1;

    internal RetryFileStoreOperationOptions CopyNormalized()
    {
        return new RetryFileStoreOperationOptions
        {
            MaxAttempts = Math.Max(1, MaxAttempts),
            Delay = TimeSpanHelper.Clamp(Delay, TimeSpan.Zero, RetryConsts.MaxDelay),
            BackoffMultiplier = Math.Max(1, BackoffMultiplier)
        };
    }

    internal static RetryFileStoreOperationOptions CreateReadDefaults()
    {
        return new RetryFileStoreOperationOptions
        {
            MaxAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(10)
        };
    }

    internal static RetryFileStoreOperationOptions CreateWriteDefaults()
    {
        return new RetryFileStoreOperationOptions
        {
            MaxAttempts = 5,
            Delay = TimeSpan.FromMilliseconds(25),
            BackoffMultiplier = 2
        };
    }
}
