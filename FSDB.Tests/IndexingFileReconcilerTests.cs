using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using FSDB.Encoding;
using FSDB.FileStorage;
using FSDB.Indexing;
using FSDB.Indexing.Reconciliation;
using FSDB.Retry;
using FSDB.Tests.TestSupport;

namespace FSDB.Tests;

public class IndexingFileReconcilerTests
{
    [Fact]
    public async Task ReconcileAsync_NewReadableFile_AddsItToIndex()
    {
        await using var fixture = await Fixture.CreateAsync();
        var path = await fixture.WriteAsync("record.json", new TestRecord("id-1", 1, "value"));

        var result = await fixture.Reconciler.ReconcileAsync(path, default);

        result.Should().Be(RetryDecision.Complete);
        fixture.Index.Records["id-1"].CurrentFileName.Should().Be("record.json");
    }

    [Fact]
    public async Task ReconcileAsync_FileChangesId_MovesItBetweenRecords()
    {
        await using var fixture = await Fixture.CreateAsync();
        var path = await fixture.WriteAsync("record.json", new TestRecord("id-1", 1, "first"));
        await fixture.Reconciler.ReconcileAsync(path, default);

        await fixture.WriteAsync("record.json", new TestRecord("id-2", 1, "second"));
        var result = await fixture.Reconciler.ReconcileAsync(path, default);

        result.Should().Be(RetryDecision.Complete);
        fixture.Index.Records.Should().NotContainKey("id-1");
        fixture.Index.Records["id-2"].CurrentFileName.Should().Be("record.json");
    }

    [Fact]
    public async Task ReconcileAsync_MissingIndexedFile_RemovesItFromIndex()
    {
        await using var fixture = await Fixture.CreateAsync();
        var path = await fixture.WriteAsync("record.json", new TestRecord("id-1", 1, "value"));
        await fixture.Reconciler.ReconcileAsync(path, default);

        File.Delete(path);
        var result = await fixture.Reconciler.ReconcileAsync(path, default);

        result.Should().Be(RetryDecision.Complete);
        fixture.Index.Records.Should().NotContainKey("id-1");
    }

    [Fact]
    public async Task ReconcileAsync_InvalidIndexedFile_StoresErrorState()
    {
        await using var fixture = await Fixture.CreateAsync();
        var path = await fixture.WriteAsync("record.json", new TestRecord("id-1", 1, "value"));
        await fixture.Reconciler.ReconcileAsync(path, default);

        await File.WriteAllTextAsync(path, "invalid json");
        var result = await fixture.Reconciler.ReconcileAsync(path, default);

        result.Should().Be(RetryDecision.Complete);
        var state = fixture.Index.Records["id-1"].GetCurrentFileState();
        state.ErrorInfo.Should().NotBeNull();
        state.ErrorInfo!.Reason.Should().Be(FileErrorReason.Invalid);
    }

    [Fact]
    public async Task ContinueAfterRead_WithHeldMatchingLock_UpdatesIndex()
    {
        await using var fixture = await Fixture.CreateAsync();
        var path = await fixture.WriteAsync("record.json", new TestRecord("id-1", 1, "first"));
        await fixture.Reconciler.ReconcileAsync(path, default);

        await fixture.WriteAsync("record.json", new TestRecord("ID-1", 1, "second"));
        var readResult = await fixture.Store.ReadAsync(path, default);
        using var sharedScope = await fixture.Index.EnterSharedScopeAsync();
        using var recordScope = await sharedScope.LockRecordAsync("id-1");

        var result = fixture.Reconciler.ContinueAfterRead(
            path,
            sharedScope,
            recordScope,
            readResult);

        result.Should().Be(RetryDecision.Complete);
        fixture.Index.Records["id-1"].GetCurrentFileState().Projection.Should().Be("second");
    }

    [Fact]
    public async Task ContinueAfterRead_WithDifferentFileId_ReturnsRetryWithoutChangingIndex()
    {
        await using var fixture = await Fixture.CreateAsync();
        var path = await fixture.WriteAsync("record.json", new TestRecord("id-1", 1, "first"));
        await fixture.Reconciler.ReconcileAsync(path, default);

        await fixture.WriteAsync("record.json", new TestRecord("id-2", 1, "second"));
        var readResult = await fixture.Store.ReadAsync(path, default);
        using var sharedScope = await fixture.Index.EnterSharedScopeAsync();
        using var recordScope = await sharedScope.LockRecordAsync("id-1");

        var result = fixture.Reconciler.ContinueAfterRead(
            path,
            sharedScope,
            recordScope,
            readResult);

        result.Should().Be(RetryDecision.RetryWithMinBackoff);
        fixture.Index.Records["id-1"].GetCurrentFileState().Projection.Should().Be("first");
        fixture.Index.Records.Should().NotContainKey("id-2");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _rootPath;

        private Fixture(
            string rootPath,
            string tablePath,
            TableIndex<string, TestRecord, string> index,
            RecordStore<string, TestRecord> store,
            FileReconciler<string, TestRecord, string> reconciler)
        {
            _rootPath = rootPath;
            TablePath = tablePath;
            Index = index;
            Store = store;
            Reconciler = reconciler;
        }

        public string TablePath { get; }
        public TableIndex<string, TestRecord, string> Index { get; }
        public RecordStore<string, TestRecord> Store { get; }
        public FileReconciler<string, TestRecord, string> Reconciler { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var rootPath = Directory.CreateTempSubdirectory().FullName;
            var tablePath = Path.Combine(rootPath, "table");
            var indexPath = Path.Combine(rootPath, "index.json");
            Directory.CreateDirectory(tablePath);

            var codec = new RecordCodec<string, TestRecord>(
                new DecoderPolicyBuilder()
                    .WithoutVersioning(TestsJsonContext.Default.TestRecord));
            var context = TestTableContext.Create<string, TestRecord, string>(
                static record => record.Value,
                static record => [record.Id],
                StringComparer.OrdinalIgnoreCase,
                StringComparer.OrdinalIgnoreCase,
                codec);
            var persistence = TestTableContext.CreateJsonIndexPersistence(
                TestsJsonContext.Default.String,
                TestsJsonContext.Default.String,
                StringComparer.OrdinalIgnoreCase);
            var engine = await RecordScopedIndexEngine<string, TestRecord, string>.StartAsync(
                indexPath,
                context,
                persistence,
                autoSaveEnabled: false);
            var index = new TableIndex<string, TestRecord, string>(engine, context);
            var fileStore = new FileStore();
            var store = new RecordStore<string, TestRecord>(codec, fileStore);
            var reconciler = new FileReconciler<string, TestRecord, string>(
                context,
                fileStore,
                store,
                index);

            return new(rootPath, tablePath, index, store, reconciler);
        }

        public async Task<string> WriteAsync(string fileName, TestRecord record)
        {
            var path = Path.Combine(TablePath, fileName);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(record, TestsJsonContext.Default.TestRecord));
            return path;
        }

        public async ValueTask DisposeAsync()
        {
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
}
