using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using FSDB.Encoding;
using FSDB.FileStorage;
using FSDB.Indexing;
using FSDB.Indexing.Reconciliation;
using FSDB.Indexing.State;
using FSDB.Retry;
using FSDB.Tests.TestSupport;

namespace FSDB.Tests;

public class IndexingFileReconcilerTests
{
    [Fact]
    public async Task ReconcileAsync_NewReadableFile_AddsItToIndex()
    {
        await using var fixture = await Fixture.CreateUnversionedAsync();
        var path = await fixture.WriteAsync("record.json", new TestRecord("id-1", 1, "value"));

        var result = await fixture.Reconciler.ReconcileAsync(path, default);

        result.Should().Be(RetryDecision.Complete);
        fixture.Index.Records["id-1"].CurrentFileName.Should().Be("record.json");
        fixture.Index.Records["id-1"].GetCurrentFileState().SchemaVersion.Should().BeNull();
    }

    [Fact]
    public async Task ReconcileAsync_WithoutVersioning_DoesNotRunFileUpdateLogic()
    {
        var scriptedStore = new ScriptedFileStore(new FileStore());
        await using var fixture = await Fixture.CreateUnversionedAsync(scriptedStore);
        var path = await fixture.WriteAsync("record.json", new TestRecord("id-1", 1, "first"));
        scriptedStore.EnqueueWriteResult(
            path,
            () => throw new InvalidOperationException(
                "File update logic must not write an unversioned file."));

        await fixture.Reconciler.ReconcileAsync(path, default);
    }

    [Theory]
    [InlineData("id-1", "id-2")]
    [InlineData("id-2", "id-1")]
    public async Task ReconcileAsync_FileChangesId_MovesItBetweenRecords(
        string indexedId,
        string fileId)
    {
        await using var fixture = await Fixture.CreateUnversionedAsync();
        var path = await fixture.WriteAsync("record.json", new TestRecord(indexedId, 1, "first"));
        await fixture.Reconciler.ReconcileAsync(path, default);

        await fixture.WriteAsync("record.json", new TestRecord(fileId, 1, "second"));
        var result = await fixture.Reconciler.ReconcileAsync(path, default);

        result.Should().Be(RetryDecision.Complete);
        fixture.Index.Records.Should().NotContainKey(indexedId);
        fixture.Index.Records[fileId].CurrentFileName.Should().Be("record.json");
        fixture.Index.Records[fileId].GetCurrentFileState().Projection.Should().Be("second");
    }

    [Fact]
    public async Task ReconcileAsync_FingerprintChangesAfterLock_UsesLatestReadResult()
    {
        var scriptedStore = new ScriptedFileStore(new FileStore());
        await using var fixture = await Fixture.CreateUnversionedAsync(scriptedStore);
        var path = await fixture.WriteAsync("record.json", new TestRecord("id-1", 1, "first"));
        var firstFingerprint = new FileStore().GetFileFingerprint(path);
        scriptedStore.EnqueueFingerprintResult(path, firstFingerprint);
        scriptedStore.EnqueueFingerprintResult(
            path,
            () => ReplaceFile(path, new TestRecord("id-1", 1, "second")));

        var result = await fixture.Reconciler.ReconcileAsync(path, default);

        result.Should().Be(RetryDecision.Complete);
        var state = fixture.Index.Records["id-1"].GetCurrentFileState();
        state.Projection.Should().Be("second");
        state.Fingerprint.Should().Be(new FileStore().GetFileFingerprint(path));
    }

    [Fact]
    public async Task ReconcileAsync_FileIdChangesAfterLock_ReturnsMinRetryWithoutMutation()
    {
        var scriptedStore = new ScriptedFileStore(new FileStore());
        await using var fixture = await Fixture.CreateUnversionedAsync(scriptedStore);
        var path = await fixture.WriteAsync("record.json", new TestRecord("id-1", 1, "first"));
        var firstFingerprint = new FileStore().GetFileFingerprint(path);
        scriptedStore.EnqueueFingerprintResult(path, firstFingerprint);
        scriptedStore.EnqueueFingerprintResult(
            path,
            () => ReplaceFile(path, new TestRecord("id-2", 1, "second")));

        var firstResult = await fixture.Reconciler.ReconcileAsync(path, default);

        firstResult.Should().Be(RetryDecision.RetryWithMinBackoff);
        fixture.Index.Records.Should().BeEmpty();

        var secondResult = await fixture.Reconciler.ReconcileAsync(path, default);

        secondResult.Should().Be(RetryDecision.Complete);
        fixture.Index.Records.Should().ContainSingle().Which.Key.Should().Be("id-2");
        fixture.Index.Records["id-2"].GetCurrentFileState().Projection.Should().Be("second");
    }

    [Fact]
    public async Task ReconcileAsync_MissingIndexedFile_RemovesItFromIndex()
    {
        await using var fixture = await Fixture.CreateUnversionedAsync();
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
        await using var fixture = await Fixture.CreateUnversionedAsync();
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
    public async Task ReconcileAsync_LegacyCurrentFile_UpdatesFileAndIndexedPhysicalState()
    {
        await using var fixture = await Fixture.CreateVersionedAsync();
        var path = await fixture.WriteLegacyAsync("record.json", new LegacyTestRecord("id-1", 0, "legacy"));

        var result = await fixture.Reconciler.ReconcileAsync(path, default);

        result.Should().Be(RetryDecision.Complete);
        var persisted = await File.ReadAllTextAsync(path);
        JsonSerializer.Deserialize(persisted, TestsJsonContext.Default.TestRecord)
            .Should().Be(new TestRecord("id-1", 1, "migrated-legacy"));
        persisted.Should().NotContain("LegacyValue");

        var state = fixture.Index.Records["id-1"].GetCurrentFileState();
        state.SchemaVersion.Should().Be(1);
        state.Fingerprint.Should().Be(new FileStore().GetFileFingerprint(path));
    }

    [Fact]
    public async Task ReconcileAsync_LegacyDormantFile_DoesNotUpdateFile()
    {
        await using var fixture = await Fixture.CreateVersionedAsync();
        var legacyPath = await fixture.WriteLegacyAsync("legacy.json", new LegacyTestRecord("id-1", 0, "legacy"));
        var currentPath = await fixture.WriteAsync("current.json", new TestRecord("id-1", 1, "current"));

        await fixture.Reconciler.ReconcileAsync(currentPath, default);
        var result = await fixture.Reconciler.ReconcileAsync(legacyPath, default);

        result.Should().Be(RetryDecision.Complete);
        fixture.Index.Records["id-1"].CurrentFileName.Should().Be("current.json");
        fixture.Index.Records["id-1"].Files["legacy.json"].SchemaVersion.Should().Be(0);
        (await File.ReadAllTextAsync(legacyPath)).Should().Contain("LegacyValue");
    }

    [Fact]
    public async Task ReconcileAsync_TransientFormatUpdateWriteFailure_ReturnsRetryAndKeepsPhysicalVersion()
    {
        var scriptedStore = new ScriptedFileStore(new FileStore());
        await using var fixture = await Fixture.CreateVersionedAsync(scriptedStore);
        var path = await fixture.WriteLegacyAsync("record.json", new LegacyTestRecord("id-1", 0, "legacy"));
        scriptedStore.EnqueueWriteResult(
            path,
            new FileWriteResult(
                null,
                new FileError(
                    FileErrorReason.Unavailable,
                    FileErrorPersistence.Transient,
                    new IOException("transient write"))));

        var firstResult = await fixture.Reconciler.ReconcileAsync(path, default);

        firstResult.Should().Be(RetryDecision.RetryWithBackoff);
        fixture.Index.Records["id-1"].GetCurrentFileState().SchemaVersion.Should().Be(0);
        (await File.ReadAllTextAsync(path)).Should().Contain("LegacyValue");

        var secondResult = await fixture.Reconciler.ReconcileAsync(path, default);

        secondResult.Should().Be(RetryDecision.Complete);
        fixture.Index.Records["id-1"].GetCurrentFileState().SchemaVersion.Should().Be(1);
    }

    [Fact]
    public async Task ReconcileAsync_CurrentVersionedFileWithMissingIndexedSchema_BackfillsSchemaVersion()
    {
        await using var fixture = await Fixture.CreateVersionedAsync();
        var record = new TestRecord("id-1", 1, "current");
        var path = await fixture.WriteAsync("record.json", record);
        var fingerprint = new FileStore().GetFileFingerprint(path);
        fixture.Engine.Upsert("id-1", "record.json", fingerprint, null, record)
            .Should().Be(IndexOperationResult.Applied);
        fixture.Index.Records["id-1"].GetCurrentFileState().SchemaVersion.Should().BeNull();

        var result = await fixture.Reconciler.ReconcileAsync(path, default);

        result.Should().Be(RetryDecision.Complete);
        fixture.Index.Records["id-1"].GetCurrentFileState().SchemaVersion.Should().Be(1);
    }

    [Fact]
    public async Task ContinueAfterRead_WithHeldMatchingLock_UpdatesIndex()
    {
        await using var fixture = await Fixture.CreateUnversionedAsync();
        var path = await fixture.WriteAsync("record.json", new TestRecord("id-1", 1, "first"));
        await fixture.Reconciler.ReconcileAsync(path, default);

        await fixture.WriteAsync("record.json", new TestRecord("ID-1", 1, "second"));
        var readResult = await fixture.Store.ReadAsync(path, default);
        using var sharedScope = await fixture.Index.EnterSharedScopeAsync();
        using var recordScope = await sharedScope.LockRecordAsync("id-1");

        var result = await fixture.Reconciler.ContinueAfterReadAsync(
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
        await using var fixture = await Fixture.CreateUnversionedAsync();
        var path = await fixture.WriteAsync("record.json", new TestRecord("id-1", 1, "first"));
        await fixture.Reconciler.ReconcileAsync(path, default);

        await fixture.WriteAsync("record.json", new TestRecord("id-2", 1, "second"));
        var readResult = await fixture.Store.ReadAsync(path, default);
        using var sharedScope = await fixture.Index.EnterSharedScopeAsync();
        using var recordScope = await sharedScope.LockRecordAsync("id-1");

        var result = await fixture.Reconciler.ContinueAfterReadAsync(
            path,
            sharedScope,
            recordScope,
            readResult);

        result.Should().Be(RetryDecision.RetryWithMinBackoff);
        fixture.Index.Records["id-1"].GetCurrentFileState().Projection.Should().Be("first");
        fixture.Index.Records.Should().NotContainKey("id-2");
    }

    [Fact]
    public async Task ContinueAfterRead_LegacyFile_UpdatesIndexAndRequestsRetry()
    {
        await using var fixture = await Fixture.CreateVersionedAsync();
        var path = await fixture.WriteLegacyAsync("record.json", new LegacyTestRecord("id-1", 0, "legacy"));
        var readResult = await fixture.Store.ReadAsync(path, default);
        using var sharedScope = await fixture.Index.EnterSharedScopeAsync();
        using var recordScope = await sharedScope.LockRecordAsync("id-1");

        var result = await fixture.Reconciler.ContinueAfterReadAsync(path, sharedScope, recordScope, readResult);

        result.Should().Be(RetryDecision.RetryWithMinBackoff);
        fixture.Index.Records["id-1"].GetCurrentFileState().SchemaVersion.Should().Be(0);
        (await File.ReadAllTextAsync(path)).Should().Contain("LegacyValue");
    }

    private static FileFingerprint ReplaceFile(
        string path,
        TestRecord record)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(record, TestsJsonContext.Default.TestRecord));
        return new FileStore().GetFileFingerprint(path);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _rootPath;

        private Fixture(
            string rootPath,
            string tablePath,
            RecordScopedIndexEngine<string, TestRecord, string> engine,
            TableIndex<string, TestRecord, string> index,
            RecordStore<string, TestRecord> store,
            FileReconciler<string, TestRecord, string> reconciler)
        {
            _rootPath = rootPath;
            TablePath = tablePath;
            Engine = engine;
            Index = index;
            Store = store;
            Reconciler = reconciler;
        }

        public string TablePath { get; }
        public RecordScopedIndexEngine<string, TestRecord, string> Engine { get; }
        public TableIndex<string, TestRecord, string> Index { get; }
        public RecordStore<string, TestRecord> Store { get; }
        public FileReconciler<string, TestRecord, string> Reconciler { get; }

        public static async Task<Fixture> CreateUnversionedAsync(IFileStore? fileStore = null)
        {
            var policy = new DecoderPolicyBuilder()
                .WithoutVersioning(TestsJsonContext.Default.TestRecord);
            return await CreateAsync(policy, fileStore);
        }

        public static async Task<Fixture> CreateVersionedAsync(IFileStore? fileStore = null)
        {
            var policy = new DecoderPolicyBuilder()
                .StartWith<LegacyTestRecord>(0)
                .UpgradeTo(1, legacy => new TestRecord(legacy.Id, 1, $"migrated-{legacy.LegacyValue}"))
                .Build();
            return await CreateAsync(policy, fileStore);
        }

        private static async Task<Fixture> CreateAsync(
            DecoderPolicy<TestRecord> policy,
            IFileStore? fileStore)
        {
            var rootPath = Directory.CreateTempSubdirectory().FullName;
            var tablePath = Path.Combine(rootPath, "table");
            var indexPath = Path.Combine(rootPath, "index.json");
            Directory.CreateDirectory(tablePath);

            var codec = new RecordCodec<string, TestRecord>(policy);
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
            var effectiveFileStore = fileStore ?? new FileStore();
            var store = new RecordStore<string, TestRecord>(codec, effectiveFileStore);
            var reconciler = new FileReconciler<string, TestRecord, string>(
                context,
                effectiveFileStore,
                store,
                index);

            return new(rootPath, tablePath, engine, index, store, reconciler);
        }

        public async Task<string> WriteAsync(string fileName, TestRecord record)
        {
            var path = Path.Combine(TablePath, fileName);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(record, TestsJsonContext.Default.TestRecord));
            return path;
        }

        public async Task<string> WriteLegacyAsync(string fileName, LegacyTestRecord record)
        {
            var path = Path.Combine(TablePath, fileName);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record));
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
