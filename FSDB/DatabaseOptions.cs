using System;
using FSDB.Files;
using FSDB.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSDB;

public sealed class DatabaseOptions
{
    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;
    public int MaxFileNameReserveAttempts { get; set; } = 5;
    public TimeSpan IndexAutoSaveInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Creates the file store used by FSDB. Override this to change low-level file access behavior,
    /// including how file access errors are classified as transient or permanent.
    /// </summary>
    public Func<IFileStore>? FileStoreFactory { get; set; }

    public Func<FileStoreRetryContext, IFileStore>? FileStoreRetryFactory { get; set; }
    public Func<ILoggerFactory, IWorkScheduler<string>>? WorkSchedulerFactory { get; set; }
}
