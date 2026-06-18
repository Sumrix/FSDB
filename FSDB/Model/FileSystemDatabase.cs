using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using FSDB.FileStorage;
using FSDB.Infrastructure.Helpers;
using FSDB.Infrastructure.Logging;
using FSDB.Infrastructure.Watching;
using FSDB.Retry;
using FSDB.Runtime;
using Microsoft.Extensions.Logging;

namespace FSDB.Model;

public class FileSystemDatabase : IAsyncDisposable
{
    private readonly string _rootPath;
    private readonly DatabaseOptions _options;
    private readonly IReadOnlyList<ITableDefinition> _tableDefinitions;
    private readonly ILogger<FileSystemDatabase> _logger;
    private readonly IFileStore _fileStore;
    private readonly IWorkScheduler<string> _workScheduler;

    private readonly Dictionary<Type, ITableEngine> _tableEnginesByType;
    private readonly Dictionary<string, ITableEngine> _tableEnginesByDirectoryName;
    private PathWatcher? _rootPathWatcher;

    private bool _disposed = false;

    /// <exception cref="ArgumentNullException">The path is null.</exception>
    /// <exception cref="ArgumentException">The system could not retrieve the absolute path.</exception>
    /// <exception cref="SecurityException">The caller does not have the required permissions.</exception>
    /// <exception cref="NotSupportedException">The path contains a format that is not supported.</exception>
    /// <exception cref="PathTooLongException">The specified path, file name, or both exceed the system-defined maximum length.</exception>
    public static async Task<FileSystemDatabase> StartAsync(
        string rootPath,
        IReadOnlyList<ITableDefinition> tableDefinitions,
        DatabaseOptions? options = null)
    {
        options ??= new DatabaseOptions();

        var logger = options.LoggerFactory.CreateLogger<FileSystemDatabase>();
        using var _ = logger.BeginMethodScope();

        logger.LogTrace("Starting: path=\"{RootPath}\" options={@Options}", rootPath, options);

        var services = CreateServices(options);

        rootPath = PathHelper.NormalizePath(rootPath);
        var db = new FileSystemDatabase(
            rootPath,
            options,
            tableDefinitions,
            services.FileStore,
            services.WorkScheduler,
            logger);

        try
        {
            await db.StartImpl();

            logger.LogDebug("Started: path=\"{RootPath}\"", rootPath);

            return db;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start FileSystemDatabase: path=\"{RootPath}\"", rootPath);
            await db.DisposeAsync();
            throw;
        }
    }

    private FileSystemDatabase(
        string rootPath,
        DatabaseOptions options,
        IReadOnlyList<ITableDefinition> tableDefinitions,
        IFileStore fileStore,
        IWorkScheduler<string> workScheduler,
        ILogger<FileSystemDatabase> logger)
    {
        _rootPath = rootPath;
        _options = options;
        _tableDefinitions = tableDefinitions;
        _fileStore = fileStore;
        _workScheduler = workScheduler;
        _logger = logger;
        _tableEnginesByType = new();
        _tableEnginesByDirectoryName = new(PathHelper.OSDependedPathComparer);
    }


    // TODO: Find all the possible exceptions
    private async Task StartImpl()
    {
        Directory.CreateDirectory(_rootPath);
        var indicesPath = Path.Combine(_rootPath, ".indices");
        Directory.CreateDirectory(indicesPath);

        foreach (var tableDefinition in _tableDefinitions)
        {
            var directoryName = PathHelper.SanitizeFileName(tableDefinition.Name);
            var tablePath = Path.Combine(_rootPath, directoryName);
            var indexFilePath = Path.Combine(indicesPath, $"{directoryName}.index.json");
            Directory.CreateDirectory(tablePath);

            var tableLoggerFactory = _options.LoggerFactory.CreateTableScopedLoggerFactory(tableDefinition.Name);

            var tableEngine = await tableDefinition.StartEngineAsync(
                tablePath,
                indexFilePath,
                _fileStore,
                _workScheduler,
                _options,
                tableLoggerFactory);

            if (!_tableEnginesByType.TryAdd(tableDefinition.RecordType, tableEngine))
            {
                throw new InvalidOperationException(
                    $"Multiple tables with the same record type detected: '{tableDefinition.RecordType.FullName}'.");
            }
            if (!_tableEnginesByDirectoryName.TryAdd(directoryName, tableEngine))
            {
                throw new InvalidOperationException(
                    $"Directory name collision detected after sanitizing table names: '{directoryName}'.");
            }
        }

        _rootPathWatcher = new PathWatcher(
            _rootPath,
            logger: _options.LoggerFactory.CreateLogger<PathWatcher>());
        _rootPathWatcher.Changed += (_, path) => HandleRootDirectoryChange(path);
        _rootPathWatcher.Error += (_, _) => RequestAllDirectoriesReconcile();
        _rootPathWatcher.EnableRaisingEvents = true;
    }

    public static IWorkScheduler<string> CreateDefaultWorkScheduler(
        DefaultWorkSchedulerOptions? options,
        ILoggerFactory loggerFactory)
    {
        var effectiveOptions = options ?? new DefaultWorkSchedulerOptions();

        return new TimeBucketQueueManager(
            intervalMs: effectiveOptions.IntervalMs,
            maxRetryIntervals: effectiveOptions.MaxRetryIntervals,
            backoffMultiplier: effectiveOptions.BackoffMultiplier,
            valueComparer: PathHelper.OSDependedPathComparer,
            loggerFactory: loggerFactory);
    }

    public static IFileStore CreateDefaultRetryFileStore(
        IFileStore inner,
        RetryFileStoreOptions? options,
        ILoggerFactory loggerFactory)
    {
        return new RetryFileStore(
            inner,
            options,
            loggerFactory.CreateLogger<RetryFileStore>());
    }

    public ITable<TKey, TRecord> Table<TKey, TRecord>()
        where TRecord : class, IRecord<TKey>
        where TKey : notnull
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (ITable<TKey, TRecord>)_tableEnginesByType[typeof(TRecord)];
    }

    public IIndexedTable<TKey, TRecord, TProjection> IndexedTable<TKey, TRecord, TProjection>()
        where TRecord : class, IRecord<TKey>
        where TKey : notnull
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (IIndexedTable<TKey, TRecord, TProjection>)_tableEnginesByType[typeof(TRecord)];
    }

    public IFileTable<TKey, TRecord> FileTable<TKey, TRecord>()
        where TRecord : class, IRecord<TKey>
        where TKey : notnull
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (IFileTable<TKey, TRecord>)_tableEnginesByType[typeof(TRecord)];
    }

    public IIndexedFileTable<TKey, TRecord, TProjection> IndexedFileTable<TKey, TRecord, TProjection>()
        where TRecord : class, IRecord<TKey>
        where TKey : notnull
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (IIndexedFileTable<TKey, TRecord, TProjection>)_tableEnginesByType[typeof(TRecord)];
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var tableEngine in _tableEnginesByType.Values)
        {
            ct.ThrowIfCancellationRequested();
            await tableEngine.FlushAsync(ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        DisposeHelper.SafeDispose(_rootPathWatcher);

        foreach (var tableEngine in _tableEnginesByType.Values)
            await DisposeHelper.SafeDispose(tableEngine);

        DisposeHelper.SafeDispose(_workScheduler);
        DisposeHelper.SafeDispose(_fileStore as IDisposable);

        GC.SuppressFinalize(this);
    }

    private void HandleRootDirectoryChange(string path)
    {
        var directoryName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(directoryName))
            return;

        if (_tableEnginesByDirectoryName.TryGetValue(directoryName, out var tableEngine))
        {
            tableEngine.RequestDirectoryReconcile();
        }
    }

    private void RequestAllDirectoriesReconcile()
    {
        foreach (var tableEngine in _tableEnginesByDirectoryName.Values)
        {
            tableEngine.RequestDirectoryReconcile();
        }
    }

    private static CreatedServices CreateServices(DatabaseOptions options)
    {
        IFileStore? fileStore = null;

        try
        {
            fileStore = options.FileStoreFactory?.Invoke() ?? new FileStore(
                options.LoggerFactory.CreateLogger<FileStore>());
            if (fileStore is null)
            {
                throw new InvalidOperationException("The configured file store factory returned null.");
            }

            var workScheduler = options.WorkSchedulerFactory is null
                ? CreateDefaultWorkScheduler(options: null, options.LoggerFactory)
                : options.WorkSchedulerFactory(options.LoggerFactory);
            if (workScheduler is null)
            {
                throw new InvalidOperationException("The configured work scheduler factory returned null.");
            }

            return new CreatedServices(fileStore, workScheduler);
        }
        catch
        {
            DisposeHelper.SafeDispose(fileStore as IDisposable);
            throw;
        }
    }

    private sealed class CreatedServices(IFileStore fileStore, IWorkScheduler<string> workScheduler) : IDisposable
    {
        private bool _disposed;

        public IFileStore FileStore { get; } = fileStore;
        public IWorkScheduler<string> WorkScheduler { get; } = workScheduler;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeHelper.SafeDispose(WorkScheduler);
            DisposeHelper.SafeDispose(FileStore as IDisposable);
        }
    }
}
