using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FSDB.Encoding;
using FSDB.FileStorage;
using FSDB.Indexing;
using FSDB.Model;
using FSDB.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSDB.Tests.TestSupport;

internal sealed class ReconcilerTestContext : IAsyncDisposable
{
    private readonly string _rootPath;
    private readonly string _tablePath;
    private readonly InlineWorkScheduler _scheduler;
    private readonly FileReconciler<string, TestRecord, string> _fileReconciler;
    private readonly DirectoryReconciler<string, TestRecord, string> _directoryReconciler;

    private ReconcilerTestContext(
        string rootPath,
        string tablePath,
        InlineWorkScheduler scheduler,
        TableIndex<string, TestRecord, string> index,
        FileReconciler<string, TestRecord, string> fileReconciler,
        DirectoryReconciler<string, TestRecord, string> directoryReconciler)
    {
        _rootPath = rootPath;
        _tablePath = tablePath;
        _scheduler = scheduler;
        _fileReconciler = fileReconciler;
        _directoryReconciler = directoryReconciler;
        TablePath = tablePath;
        Scheduler = scheduler;
        Index = index;
    }

    public string TablePath { get; }

    public TableIndex<string, TestRecord, string> Index { get; }

    public InlineWorkScheduler Scheduler { get; }

    public void RequestFileReconcile(string path)
    {
        _scheduler.Enqueue(path, _fileReconciler.ReconcileAsync);
    }

    public void RequestDirectoryReconcile()
    {
        _scheduler.Enqueue(_tablePath, (_, ct) => _directoryReconciler.ReconcileAsync(ct));
    }

    public static async Task<ReconcilerTestContext> CreateAsync(IFileStore? fileStore = null)
    {
        var decoderPolicy = new DecoderPolicyBuilder()
            .StartWith<LegacyTestRecord>(0)
            .UpgradeTo(1, legacy => new TestRecord(legacy.Id, 1, $"migrated-{legacy.LegacyValue}"))
            .Build();
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var tablePath = Path.Combine(rootPath, "table");
        var indexPath = Path.Combine(rootPath, "index-state.json");
        Directory.CreateDirectory(tablePath);
        var codec = new RecordCodec<string, TestRecord>(decoderPolicy);
        var table = TestTableContext.Create<string, TestRecord, string>(
            static value => value.Value,
            static record => [record.Id],
            StringComparer.Ordinal,
            StringComparer.Ordinal,
            codec);

        var indexEngine = await RecordScopedIndexEngine<string, TestRecord, string>.StartAsync(
            indexPath,
            table,
            TestTableContext.CreateJsonIndexPersistence(
                TestsJsonContext.Default.String,
                TestsJsonContext.Default.String,
                StringComparer.Ordinal),
            autoSaveEnabled: false);
        var index = new TableIndex<string, TestRecord, string>(indexEngine, table);

        var effectiveFileStore = fileStore ?? new FileStore();
        var store = new RecordStore<string, TestRecord>(codec, effectiveFileStore);
        var scheduler = new InlineWorkScheduler();
        var fileReconciler = new FileReconciler<string, TestRecord, string>(
            table,
            effectiveFileStore,
            store,
            index,
            NullLogger<FileReconciler<string, TestRecord, string>>.Instance);
        var directoryReconciler = new DirectoryReconciler<string, TestRecord, string>(
            tablePath,
            index,
            fileReconciler,
            path => scheduler.Enqueue(path, fileReconciler.ReconcileAsync),
            NullLogger<DirectoryReconciler<string, TestRecord, string>>.Instance);

        return new ReconcilerTestContext(rootPath, tablePath, scheduler, index, fileReconciler, directoryReconciler);
    }

    public async Task<string> WriteRecordAsync(string fileName, TestRecord record)
    {
        var path = Path.Combine(TablePath, fileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record, TestsJsonContext.Default.TestRecord));
        return path;
    }

    public async Task<string> WriteLegacyRecordAsync(string fileName, LegacyTestRecord record)
    {
        var path = Path.Combine(TablePath, fileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record));
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        Scheduler.Dispose();
        await Index.DisposeAsync();
        try
        {
            Directory.Delete(_rootPath, recursive: true);
        }
        catch
        {
        }
    }
}

internal sealed record LegacyTestRecord(string Id, int SchemaVersion, string LegacyValue) : IVersionedRecord;
