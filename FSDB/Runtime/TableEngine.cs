using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FSDB.FileStorage;
using FSDB.Indexing;
using FSDB.Indexing.State;
using FSDB.Infrastructure.Collections;
using FSDB.Infrastructure.Helpers;
using FSDB.Infrastructure.Logging;
using FSDB.Infrastructure.Watching;
using FSDB.Model;
using FSDB.Model.Building;
using FSDB.Retry;
using Microsoft.Extensions.Logging;

namespace FSDB.Runtime;

public class TableEngine<TKey, TRecord, TProjection> :
    ITableEngine,
    IIndexedTable<TKey, TRecord, TProjection>,
    IIndexedFileTable<TKey, TRecord, TProjection>
    where TRecord : class, IRecord<TKey>
    where TKey : notnull
{
    private readonly string _tablePath;
    private readonly IRetryScheduler<string> _retryScheduler;
    private readonly TableIndex<TKey, TRecord, TProjection> _index;
    private readonly FileReconciler<TKey, TRecord, TProjection> _fileReconciler;
    private readonly DirectoryReconciler<TKey, TRecord, TProjection> _directoryReconciler;
    private readonly DatabaseOperationProcessor<TKey, TRecord, TProjection> _dbProcessor;
    private readonly FileOperationProcessor<TKey, TRecord, TProjection> _fileProcessor;
    private readonly PathWatcher _watcher;
    private readonly IReadOnlyDictionary<TKey, IndexEntry<TProjection>> _indexEntries;
    private readonly IReadOnlyDictionary<TKey, TProjection> _projections;

    private bool _disposed;

    public IReadOnlyDictionary<TKey, TProjection> Index => _projections;
    IReadOnlyDictionary<TKey, IndexEntry<TProjection>> IIndexedFileTable<TKey, TRecord, TProjection>.Index => _indexEntries;

    private TableEngine(
        string tablePath,
        IRetryScheduler<string> retryScheduler,
        TableIndex<TKey, TRecord, TProjection> index,
        FileReconciler<TKey, TRecord, TProjection> fileReconciler,
        DirectoryReconciler<TKey, TRecord, TProjection> directoryReconciler,
        DatabaseOperationProcessor<TKey, TRecord, TProjection> dbProcessor,
        FileOperationProcessor<TKey, TRecord, TProjection> fileProcessor,
        PathWatcher watcher)
    {
        _tablePath = tablePath;
        _retryScheduler = retryScheduler;
        _index = index;
        _fileReconciler = fileReconciler;
        _directoryReconciler = directoryReconciler;
        _dbProcessor = dbProcessor;
        _fileProcessor = fileProcessor;
        _watcher = watcher;
        _indexEntries = new MappedDictionaryView<TKey, IReadOnlyRecordIndexState<TKey, TProjection>, IndexEntry<TProjection>>(
            _index.Records, TryCreateIndexEntry);
        _projections = new MappedDictionaryView<TKey, IReadOnlyRecordIndexState<TKey, TProjection>, TProjection>(
            _index.Records, TryCreateProjection);
    }

    public static async Task<TableEngine<TKey, TRecord, TProjection>> StartAsync(
        string tablePath,
        string indexFilePath,
        TableDefinition<TKey, TRecord, TProjection> definition,
        IFileStore storage,
        IRetryScheduler<string> retryScheduler,
        DatabaseOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var log = loggerFactory.CreateLogger<TableEngine<TKey, TRecord, TProjection>>();
        using var _ = log.BeginMethodScope();

        Directory.CreateDirectory(tablePath);

        var tableContext = CreateTableContext(definition, loggerFactory);
        var rawStore = new RecordStore<TKey, TRecord>(
            tableContext.RecordCodec,
            storage);
        var operationStore = new RecordStore<TKey, TRecord>(
            tableContext.RecordCodec,
            CreateRetryFileStore(
                storage,
                options,
                loggerFactory));
        var index = await CreateIndexAsync(indexFilePath, definition, tableContext, options, loggerFactory, ct);

        var fileReconciler = new FileReconciler<TKey, TRecord, TProjection>(
            tableContext,
            storage,
            rawStore,
            index,
            loggerFactory.CreateLogger<FileReconciler<TKey, TRecord, TProjection>>());
        var directoryReconciler = new DirectoryReconciler<TKey, TRecord, TProjection>(
            tablePath,
            index,
            fileReconciler,
            path => retryScheduler.Enqueue(path, fileReconciler.ReconcileAsync),
            loggerFactory.CreateLogger<DirectoryReconciler<TKey, TRecord, TProjection>>());

        var fileProcessor = new FileOperationProcessor<TKey, TRecord, TProjection>(
            tablePath,
            tableContext,
            index,
            operationStore,
            options.MaxFileNameReserveAttempts,
            path => retryScheduler.Enqueue(path, fileReconciler.ReconcileAsync),
            loggerFactory.CreateLogger<FileOperationProcessor<TKey, TRecord, TProjection>>());

        var dbProcessor = new DatabaseOperationProcessor<TKey, TRecord, TProjection>(
            fileProcessor);

        var watcher = new PathWatcher(
            tablePath,
            ".json",
            loggerFactory.CreateLogger<PathWatcher>());
        var tableEngine = new TableEngine<TKey, TRecord, TProjection>(
            tablePath,
            retryScheduler,
            index,
            fileReconciler,
            directoryReconciler,
            dbProcessor,
            fileProcessor,
            watcher);
        watcher.Changed += (_, path) => tableEngine.RequestFileReconcile(path);
        watcher.Error += (_, _) => tableEngine.RequestDirectoryReconcile();
        watcher.EnableRaisingEvents = true;

        tableEngine.RequestDirectoryReconcile();

        return tableEngine;
    }

    private static IFileStore CreateRetryFileStore(
        IFileStore inner,
        DatabaseOptions databaseOptions,
        ILoggerFactory loggerFactory)
    {
        if (databaseOptions.FileStoreRetryFactory is null)
        {
            return FileSystemDatabase.CreateDefaultRetryFileStore(
                inner,
                options: null,
                loggerFactory);
        }

        var retryStore = databaseOptions.FileStoreRetryFactory(new FileStoreRetryContext
        {
            Inner = inner,
            LoggerFactory = loggerFactory
        });
        if (retryStore is null)
        {
            throw new InvalidOperationException("The configured file store retry factory returned null.");
        }

        return retryStore;
    }

    private static TableContext<TKey, TRecord, TProjection> CreateTableContext(
        TableDefinition<TKey, TRecord, TProjection> definition,
        ILoggerFactory loggerFactory)
    {
        var recordContext = new RecordCodecContext(loggerFactory);
        var recordCodec = definition.RecordCodecFactory(recordContext);
        if (recordCodec is null)
        {
            throw new InvalidOperationException(
                $"'{definition.Name}' table's defined {nameof(definition.RecordCodecFactory)} does not provide record codec.");
        }

        return new TableContext<TKey, TRecord, TProjection>
        {
            Name = definition.Name,
            KeyComparer = definition.KeyComparer,
            KeyEqualityComparer = definition.KeyEqualityComparer,
            FileNameGenerator = definition.FileNameGenerator,
            CreateProjection = definition.CreateProjection,
            RecordCodec = recordCodec,
        };
    }

    private static async Task<TableIndex<TKey, TRecord, TProjection>> CreateIndexAsync(
        string indexFilePath,
        TableDefinition<TKey, TRecord, TProjection> definition,
        TableContext<TKey, TRecord, TProjection> tableContext,
        DatabaseOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        IRecordScopedIndexEngine<TKey, TRecord, TProjection> engine;

        if (definition.IndexPersistenceFactory is not null)
        {
            var ctx = new TableIndexPersistenceContext<TKey, TRecord, TProjection>(tableContext, loggerFactory);
            var persistence = definition.IndexPersistenceFactory(ctx);
            if (persistence is null)
            {
                throw new InvalidOperationException(
                    $"'{tableContext.Name}' table's defined {nameof(definition.IndexPersistenceFactory)} does not provide index persistence.");
            }

            engine = await RecordScopedIndexEngine<TKey, TRecord, TProjection>.StartAsync(
                indexFilePath,
                tableContext,
                persistence,
                options.IndexAutoSaveInterval,
                loggerFactory,
                autoSaveEnabled: true,
                ct);
        }
        else if (definition.IndexEngineFactory is not null)
        {
            var ctx = new RecordScopedIndexEngineContext<TKey, TRecord, TProjection>
            {
                Table = tableContext,
                DatabaseOptions = options,
                IndexFilePath = indexFilePath,
                CancellationToken = ct
            };
            engine = await definition.IndexEngineFactory(ctx);
            if (engine is null)
            {
                throw new InvalidOperationException(
                    $"'{tableContext.Name}' table's defined {nameof(definition.IndexEngineFactory)} does not provide index engine.");
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"'{tableContext.Name}' table definition must provide either {nameof(definition.IndexPersistenceFactory)} or {nameof(definition.IndexEngineFactory)} to create the index engine.");
        }

        return new TableIndex<TKey, TRecord, TProjection>(engine, tableContext);
    }

    public Task<TRecord?> GetAsync(TKey id, CancellationToken ct = default) => _dbProcessor.GetAsync(id, ct);

    public Task UpsertAsync(TRecord record, CancellationToken ct = default) => _dbProcessor.UpsertAsync(record, ct);

    public Task DeleteAsync(TKey id, CancellationToken ct = default) => _dbProcessor.DeleteAsync(id, ct);

    public Task<ReadResult<TRecord>> ReadAsync(TKey id, CancellationToken ct = default) => _fileProcessor.ReadAsync(id, ct);

    public Task<OperationResult> WriteAsync(TRecord record, CancellationToken ct = default) => _fileProcessor.WriteAsync(record, ct);

    public Task<OperationResult> RemoveAsync(TKey id, CancellationToken ct = default) => _fileProcessor.RemoveAsync(id, ct);

    public Task FlushAsync(CancellationToken ct = default) => _index.FlushAsync(ct);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        DisposeHelper.SafeDispose(_watcher);
        await DisposeHelper.SafeDispose(_index);
    }

    public void RequestDirectoryReconcile()
    {
        _retryScheduler.Enqueue(_tablePath, ReconcileDirectoryAsync);
    }

    private void RequestFileReconcile(string path)
    {
        _retryScheduler.Enqueue(path, ReconcileFileAsync);
    }

    private Task<RetryDecision> ReconcileFileAsync(string path, CancellationToken ct)
    {
        return _fileReconciler.ReconcileAsync(path, ct);
    }

    private Task<RetryDecision> ReconcileDirectoryAsync(string _, CancellationToken ct)
    {
        return _directoryReconciler.ReconcileAsync(ct);
    }

    private static bool TryCreateProjection(
        IReadOnlyRecordIndexState<TKey, TProjection> record,
        [MaybeNullWhen(false)] out TProjection projection)
    {
        var fileState = record.GetCurrentFileState();
        if (fileState.Status == FileIndexStatus.Committed && fileState.ErrorInfo is null)
        {
            projection = fileState.Projection!;
            return true;
        }

        projection = default;
        return false;
    }

    private static bool TryCreateIndexEntry(
        IReadOnlyRecordIndexState<TKey, TProjection> record,
        [MaybeNullWhen(false)] out IndexEntry<TProjection> entry)
    {
        var fileState = record.GetCurrentFileState();
        if (fileState.Status != FileIndexStatus.Reserved)
        {
            entry = new(fileState.Projection, fileState.ErrorInfo, record.CurrentFileName);
            return true;
        }

        entry = null;
        return false;
    }
}
