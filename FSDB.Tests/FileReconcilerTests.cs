using System;
using System.IO;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using FSDB.Files;
using FSDB.Index.State;
using FSDB.Tests.TestSupport;
using FSDB.Tables;

namespace FSDB.Tests;

public class FileReconcilerTests
{
    [Fact]
    public async Task RequestFileReconcile_WhenNewValidFile_AddsRecordToIndex()
    {
        await using var ctx = await ReconcilerTestContext.CreateAsync();
        var filePath = await ctx.WriteRecordAsync("alpha.json", new("id-1", 1, "value"));

        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using var scope = await ctx.Index.EnterSharedScopeAsync();
        var record = Assert.Single(scope.Records).Value;
        Assert.Equal("id-1", record.Id);
        Assert.Equal("alpha.json", record.CurrentFileName);
        Assert.True(record.Files.TryGetValue("alpha.json", out var file));
        Assert.True(file.Fingerprint.Exists);
        Assert.Equal("value", file.Projection);
        Assert.Equal("value", new ProjectionIndexView<string, string>(ctx.Index.Records)["id-1"]);
    }

    [Fact]
    public async Task RequestFileReconcile_WhenKnownFileDeleted_RemovesRecordFromIndex()
    {
        await using var ctx = await ReconcilerTestContext.CreateAsync();
        var filePath = await ctx.WriteRecordAsync("alpha.json", new("id-1", 1, "value"));
        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        File.Delete(filePath);

        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using var scope = await ctx.Index.EnterSharedScopeAsync();
        Assert.Empty(scope.Records);
        Assert.Empty(scope.Files);
    }

    [Fact]
    public async Task RequestFileReconcile_WhenFileDisappearsDuringRead_RemovesRecordFromIndex()
    {
        var innerStore = new FileStore();
        var scriptedStore = new ScriptedFileStore(innerStore);
        await using var ctx = await ReconcilerTestContext.CreateAsync(scriptedStore);
        var filePath = await ctx.WriteRecordAsync("alpha.json", new("id-1", 1, "value"));
        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        scriptedStore.EnqueueFingerprintResult(
            filePath,
            new FileFingerprint(DateTime.UtcNow.AddMinutes(1), 123, Exists: true));
        scriptedStore.EnqueueReadAccessResult(
            filePath,
            FileErrorPersistence.Persistent,
            new FileFingerprint(null, null, Exists: false));
        scriptedStore.EnqueueFingerprintResult(
            filePath,
            new FileFingerprint(null, null, Exists: false));

        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using var scope = await ctx.Index.EnterSharedScopeAsync();
        Assert.Empty(scope.Records);
        Assert.Empty(scope.Files);
    }

    [Fact]
    public async Task RequestFileReconcile_WhenKnownFileBecomesInvalid_MarksItInvalid()
    {
        await using var ctx = await ReconcilerTestContext.CreateAsync();
        var filePath = await ctx.WriteRecordAsync("alpha.json", new("id-1", 1, "valid"));
        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        await File.WriteAllTextAsync(filePath, "{ invalid json");

        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using var scope = await ctx.Index.EnterSharedScopeAsync();
        var record = Assert.Single(scope.Records).Value;
        Assert.Equal("id-1", record.Id);
        Assert.Equal("alpha.json", record.CurrentFileName);
        var file = Assert.Single(scope.Files).Value;
        Assert.Equal(FileIndexStatus.Committed, file.Status);
        Assert.Equal(FileErrorReason.Invalid, file.ErrorInfo?.Reason);
    }

    [Fact]
    public async Task RequestFileReconcile_WhenKnownFileContentCannotBeRead_MarksItUnavailable()
    {
        var scriptedStore = new ScriptedFileStore();
        await using var ctx = await ReconcilerTestContext.CreateAsync(scriptedStore);
        var filePath = await ctx.WriteRecordAsync("alpha.json", new("id-1", 1, "value"));
        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        var exception = new UnauthorizedAccessException("access denied");
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(1));
        scriptedStore.EnqueueReadAccessResult(filePath, FileErrorPersistence.Persistent, exception);

        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using var scope = await ctx.Index.EnterSharedScopeAsync();
        var record = Assert.Single(scope.Records).Value;
        Assert.Equal("id-1", record.Id);
        Assert.Equal("alpha.json", record.CurrentFileName);
        var file = Assert.Single(scope.Files).Value;
        Assert.Equal(FileIndexStatus.Committed, file.Status);
        Assert.Equal(FileErrorReason.Unavailable, file.ErrorInfo?.Reason);
        Assert.Equal(typeof(UnauthorizedAccessException).FullName, file.ErrorInfo?.ExceptionType);
        Assert.Equal(exception.HResult, file.ErrorInfo?.HResult);
    }

    [Fact]
    public async Task RequestFileReconcile_WhenFileIdChanges_ReassignsFileToNewRecord()
    {
        await using var ctx = await ReconcilerTestContext.CreateAsync();
        var filePath = await ctx.WriteRecordAsync("alpha.json", new("id-old", 1, "one"));
        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        await ctx.WriteRecordAsync("alpha.json", new("id-new-longer", 1, "two"));

        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using var scope = await ctx.Index.EnterSharedScopeAsync();
        Assert.Single(scope.Records);
        Assert.True(scope.Records.ContainsKey("id-new-longer"));
        Assert.False(scope.Records.ContainsKey("id-old"));
        Assert.Equal("id-new-longer", scope.Files["alpha.json"].Record.Id);
        Assert.Equal("two", scope.Files["alpha.json"].Projection);
    }

    [Fact]
    public async Task RequestFileReconcile_WhenFileHasLegacySchema_MigratesAndPersistsLatestVersion()
    {
        await using var ctx = await ReconcilerTestContext.CreateAsync();
        var filePath = await ctx.WriteLegacyRecordAsync("alpha.json", new("id-1", 0, "legacy"));

        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using (var scope = await ctx.Index.EnterSharedScopeAsync())
        {
            var record = Assert.Single(scope.Records).Value;
            Assert.Equal("id-1", record.Id);
            Assert.Equal("alpha.json", record.CurrentFileName);
            Assert.Equal("migrated-legacy", scope.Files["alpha.json"].Projection);
        }

        var persisted = await File.ReadAllTextAsync(filePath);
        var persistedRecord = JsonSerializer.Deserialize(persisted, TestsJsonContext.Default.TestRecord);
        Assert.Equal(new TestRecord("id-1", 1, "migrated-legacy"), persistedRecord);
        Assert.DoesNotContain("LegacyValue", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestFileReconcile_WhenObservedFingerprintChangesDuringProcessing_RequeuesWithMinBackoff()
    {
        var innerStore = new FileStore();
        var scriptedStore = new ScriptedFileStore(innerStore);
        await using var ctx = await ReconcilerTestContext.CreateAsync(scriptedStore);
        var filePath = await ctx.WriteRecordAsync("alpha.json", new("id-1", 1, "value"));

        scriptedStore.EnqueueFingerprintResult(filePath, () => innerStore.GetFileFingerprint(filePath));
        scriptedStore.EnqueueFingerprintResult(
            filePath,
            new FileFingerprint(DateTime.UtcNow.AddMinutes(1), 999, Exists: true));

        ctx.RequestFileReconcile(filePath);
        var hadWork = await ctx.Scheduler.RunNextAsync();

        Assert.True(hadWork);
        Assert.Equal(1, ctx.Scheduler.PendingCount);

        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using var scope = await ctx.Index.EnterSharedScopeAsync();
        Assert.Single(scope.Records);
        Assert.True(scope.Records.ContainsKey("id-1"));
        Assert.Equal("alpha.json", scope.Records["id-1"].CurrentFileName);
    }

    [Fact]
    public async Task RequestFileReconcile_WhenFileIsLocked_EnqueuesRetry()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var ctx = await ReconcilerTestContext.CreateAsync();
        var filePath = await ctx.WriteRecordAsync("alpha.json", new("id-1", 1, "value"));

        await using (var fileLock = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ctx.RequestFileReconcile(filePath);
            var hadWork = await ctx.Scheduler.RunNextAsync();

            Assert.True(hadWork);
            Assert.Equal(1, ctx.Scheduler.PendingCount);
        }

        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using var scope = await ctx.Index.EnterSharedScopeAsync();
        Assert.Single(scope.Records);
        Assert.True(scope.Records.ContainsKey("id-1"));
    }

    [Fact]
    public async Task RequestFileReconcile_WhenUpgradeWriteIsDeferred_LaterReconcilePersistsUpgrade()
    {
        var scriptedStore = new ScriptedFileStore();
        await using var ctx = await ReconcilerTestContext.CreateAsync(scriptedStore);
        var filePath = await ctx.WriteLegacyRecordAsync("alpha.json", new("id-1", 0, "legacy"));
        scriptedStore.EnqueueWriteResult(filePath, new FileWriteResult(
            null,
            new FileError(
                FileErrorReason.Unavailable,
                FileErrorPersistence.Transient,
                new IOException("transient write"))));

        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using (var scope = await ctx.Index.EnterSharedScopeAsync())
        {
            var record = Assert.Single(scope.Records).Value;
            Assert.Equal("id-1", record.Id);
            Assert.Equal("migrated-legacy", scope.Files["alpha.json"].Projection);
        }

        var persistedAfterDeferredWrite = await File.ReadAllTextAsync(filePath);
        Assert.Contains("LegacyValue", persistedAfterDeferredWrite, StringComparison.Ordinal);

        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(1));

        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        var persisted = await File.ReadAllTextAsync(filePath);
        var persistedRecord = JsonSerializer.Deserialize(persisted, TestsJsonContext.Default.TestRecord);
        Assert.Equal(new TestRecord("id-1", 1, "migrated-legacy"), persistedRecord);
        Assert.DoesNotContain("LegacyValue", persisted, StringComparison.Ordinal);
    }

    private static void AssertNoRetry(ReconcilerTestContext ctx) => Assert.Equal(0, ctx.Scheduler.PendingCount);
}
