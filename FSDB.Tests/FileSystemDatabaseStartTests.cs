using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FSDB.FileStorage;
using FSDB.Tests.TestSupport;
using FSDB.Model;
using FSDB.Model.Building;
using FSDB.Retry;

namespace FSDB.Tests;

public sealed class FileSystemDatabaseStartTests
{
    [Fact]
    public async Task StartAsync_WithoutOptions_UsesDefaultServices()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;

        try
        {
            await using var db = await FileSystemDatabase.StartAsync(rootPath, [CreateTableDefinition()]);
            var table = db.IndexedTable<string, PlainTestRecord, NoProjection>();

            await table.UpsertAsync(new PlainTestRecord("id-1", "value-1"));
            var record = await table.GetAsync("id-1");

            Assert.Equal("value-1", record?.Value);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task Table_WhenRecordWritten_ExposesDetailedIndexState()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;

        try
        {
            await using var db = await FileSystemDatabase.StartAsync(rootPath, [CreateTableDefinition()]);
            var table = db.IndexedFileTable<string, PlainTestRecord, NoProjection>();

            await table.WriteAsync(new PlainTestRecord("id-1", "value-1"));

            var entry = Assert.Single(table.Index).Value;
            Assert.Null(entry.ErrorInfo);
            Assert.NotNull(entry.FileName);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task StartAsync_WithRetrySchedulerFactory_DisposesOwnedInstance()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        TrackingRetryScheduler? retryScheduler = null;

        try
        {
            var options = new DatabaseOptions
            {
                RetrySchedulerFactory = _ => retryScheduler = new TrackingRetryScheduler()
            };

            await using (await FileSystemDatabase.StartAsync(rootPath, [CreateTableDefinition()], options))
            {
            }

            Assert.NotNull(retryScheduler);
            Assert.True(retryScheduler!.Disposed);
            Assert.True(retryScheduler.EnqueueCount > 0);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task StartAsync_WithFileStoreFactory_DisposesOwnedInstance()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        TrackingFileStore? fileStore = null;

        try
        {
            var options = new DatabaseOptions
            {
                FileStoreFactory = () => fileStore = new TrackingFileStore()
            };

            await using (await FileSystemDatabase.StartAsync(rootPath, [CreateTableDefinition()], options))
            {
            }

            Assert.NotNull(fileStore);
            Assert.True(fileStore!.Disposed);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task StartAsync_WithFileStoreRetryFactory_UsesFactoryForDatabaseOperationStore()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        TrackingRetryFileStore? retryStore = null;

        try
        {
            var options = new DatabaseOptions
            {
                FileStoreRetryFactory = ctx =>
                {
                    retryStore = new TrackingRetryFileStore(ctx.Inner);
                    return retryStore;
                }
            };

            await using var db = await FileSystemDatabase.StartAsync(rootPath, [CreateTableDefinition()], options);
            var table = db.Table<string, PlainTestRecord>();

            await table.UpsertAsync(new PlainTestRecord("id-1", "value-1"));
            var record = await table.GetAsync("id-1");

            Assert.Equal("value-1", record?.Value);
            Assert.NotNull(retryStore);
            Assert.True(retryStore!.ReadCount > 0);
            Assert.True(retryStore.WriteCount > 0);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CreateDefaultRetryFileStore_WithOptions_CreatesUsableRetryStore()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var fileStore = new TransientFirstWriteFileStore(new FileStore());
        var retryOptions = new RetryFileStoreOptions
        {
            Write = new RetryFileStoreOperationOptions
            {
                MaxAttempts = 2,
                Delay = TimeSpan.Zero
            }
        };

        try
        {
            var options = new DatabaseOptions
            {
                FileStoreFactory = () => fileStore,
                FileStoreRetryFactory = ctx => FileSystemDatabase.CreateDefaultRetryFileStore(
                    ctx.Inner,
                    retryOptions,
                    ctx.LoggerFactory)
            };

            await using var db = await FileSystemDatabase.StartAsync(rootPath, [CreateTableDefinition()], options);
            var table = db.Table<string, PlainTestRecord>();

            await table.UpsertAsync(new PlainTestRecord("id-1", "value-1"));

            Assert.Equal(2, fileStore.WriteAttempts);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task StartAsync_WithDefaultRetrySchedulerFactory_AssignsFactoryToPassedOptions()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;

        try
        {
            var retrySchedulerOptions = new DefaultRetrySchedulerOptions
            {
                IntervalMs = 100,
                MaxRetryIntervals = 10,
                BackoffMultiplier = 2
            };
            var options = new DatabaseOptions
            {
                RetrySchedulerFactory = loggerFactory => FileSystemDatabase.CreateDefaultRetryScheduler(
                    retrySchedulerOptions,
                    loggerFactory)
            };

            await using var db = await FileSystemDatabase.StartAsync(
                rootPath,
                [CreateTableDefinition()],
                options);

            var table = db.Table<string, PlainTestRecord>();
            await table.UpsertAsync(new PlainTestRecord("id-1", "value-1"));

            Assert.Equal("value-1", (await table.GetAsync("id-1"))?.Value);
            Assert.NotNull(options.RetrySchedulerFactory);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CreateDefaultRetryScheduler_WithOptions_CreatesUsableScheduler()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;

        try
        {
            var options = new DatabaseOptions
            {
                RetrySchedulerFactory = loggerFactory => FileSystemDatabase.CreateDefaultRetryScheduler(
                    new DefaultRetrySchedulerOptions
                    {
                        IntervalMs = 100,
                        MaxRetryIntervals = 10,
                        BackoffMultiplier = 2
                    },
                    loggerFactory)
            };

            await using var db = await FileSystemDatabase.StartAsync(
                rootPath,
                [CreateTableDefinition()],
                options);

            var table = db.Table<string, PlainTestRecord>();
            await table.UpsertAsync(new PlainTestRecord("id-1", "value-1"));

            Assert.Equal("value-1", (await table.GetAsync("id-1"))?.Value);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task StartAsync_WithDefaultRetrySchedulerFactory_DoesNotUsePreviousRetrySchedulerFactory()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        TrackingRetryScheduler? originalScheduler = null;
        var originalFactory = new Func<Microsoft.Extensions.Logging.ILoggerFactory, IRetryScheduler<string>>(
            _ => originalScheduler = new TrackingRetryScheduler());

        try
        {
            var options = new DatabaseOptions
            {
                RetrySchedulerFactory = loggerFactory => FileSystemDatabase.CreateDefaultRetryScheduler(
                    new DefaultRetrySchedulerOptions(),
                    loggerFactory)
            };

            await using (await FileSystemDatabase.StartAsync(rootPath, [CreateTableDefinition()], options))
            {
            }

            Assert.Null(originalScheduler);
            Assert.NotNull(options.RetrySchedulerFactory);
            Assert.NotSame(originalFactory, options.RetrySchedulerFactory);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static TableDefinition<string, PlainTestRecord, NoProjection> CreateTableDefinition()
    {
        return TableDefinitionBuilder.CreateDefault<string, PlainTestRecord>(
            jsonOptions: TestsJsonContext.Default.Options);
    }

    private sealed class TrackingRetryScheduler : IRetryScheduler<string>
    {
        public bool Disposed { get; private set; }
        public int EnqueueCount { get; private set; }

        public void Enqueue(string value, Func<string, CancellationToken, Task<RetryDecision>> processor)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            EnqueueCount++;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class TrackingFileStore : IFileStore, IDisposable
    {
        private readonly FileStore _inner = new();

        public bool Disposed { get; private set; }

        public Task<FileWriteResult> WriteAsync(
            string path,
            Func<Stream, Task> writeAction,
            CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            return _inner.WriteAsync(path, writeAction, ct);
        }

        public Task<FileReadResult<T>> ReadAsync<T>(string path, Func<Stream, Task<T>> parseAction, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            return _inner.ReadAsync(path, parseAction, ct);
        }

        public Task<FileDeleteResult> DeleteAsync(string path, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            return _inner.DeleteAsync(path, ct);
        }

        public FileFingerprint GetFileFingerprint(string path)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            return _inner.GetFileFingerprint(path);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class TrackingRetryFileStore(IFileStore inner) : IFileStore
    {
        public int WriteCount { get; private set; }
        public int ReadCount { get; private set; }

        public Task<FileWriteResult> WriteAsync(
            string path,
            Func<Stream, Task> writeAction,
            CancellationToken ct)
        {
            WriteCount++;
            return inner.WriteAsync(path, writeAction, ct);
        }

        public Task<FileReadResult<T>> ReadAsync<T>(string path, Func<Stream, Task<T>> parseAction, CancellationToken ct)
        {
            ReadCount++;
            return inner.ReadAsync(path, parseAction, ct);
        }

        public Task<FileDeleteResult> DeleteAsync(string path, CancellationToken ct)
        {
            return inner.DeleteAsync(path, ct);
        }

        public FileFingerprint GetFileFingerprint(string path)
        {
            return inner.GetFileFingerprint(path);
        }
    }

    private sealed class TransientFirstWriteFileStore(IFileStore inner) : IFileStore
    {
        public int WriteAttempts { get; private set; }

        public Task<FileWriteResult> WriteAsync(
            string path,
            Func<Stream, Task> writeAction,
            CancellationToken ct)
        {
            WriteAttempts++;
            return WriteAttempts == 1
                ? Task.FromResult(new FileWriteResult(
                    null,
                    new FileError(
                        FileErrorReason.Unavailable,
                        FileErrorPersistence.Transient,
                        new IOException("transient write"))))
                : inner.WriteAsync(path, writeAction, ct);
        }

        public Task<FileReadResult<T>> ReadAsync<T>(string path, Func<Stream, Task<T>> parseAction, CancellationToken ct)
        {
            return inner.ReadAsync(path, parseAction, ct);
        }

        public Task<FileDeleteResult> DeleteAsync(string path, CancellationToken ct)
        {
            return inner.DeleteAsync(path, ct);
        }

        public FileFingerprint GetFileFingerprint(string path)
        {
            return inner.GetFileFingerprint(path);
        }
    }
}
