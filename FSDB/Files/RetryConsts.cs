using System;

namespace FSDB.Files;

internal static class RetryConsts
{
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);
}