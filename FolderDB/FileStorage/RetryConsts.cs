using System;

namespace FolderDB.FileStorage;

internal static class RetryConsts
{
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);
}