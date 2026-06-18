using System;

namespace FSDB.FileStorage;

internal static class RetryConsts
{
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);
}