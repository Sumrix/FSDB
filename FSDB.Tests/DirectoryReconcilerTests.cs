using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FSDB.Runtime;
using FSDB.Tests.TestSupport;

namespace FSDB.Tests;

public class DirectoryReconcilerTests
{
    [Fact]
    public async Task RequestDirectoryReconcile_WhenTableDirectoryMissing_ClearsIndex()
    {
        await using var ctx = await ReconcilerTestContext.CreateAsync();
        var filePath = await ctx.WriteRecordAsync("alpha.json", new("id-1", 1, "value"));
        ctx.RequestFileReconcile(filePath);
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        Directory.Delete(ctx.TablePath, recursive: true);

        ctx.RequestDirectoryReconcile();
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using var scope = await ctx.Index.EnterSharedScopeAsync();
        Assert.Empty(scope.Records);
        Assert.Empty(scope.Files);
    }

    [Fact]
    public async Task RequestDirectoryReconcile_WhenIndexedFileDeleted_RemovesDeletedFileFromIndex()
    {
        await using var ctx = await ReconcilerTestContext.CreateAsync();
        await ctx.WriteRecordAsync("a.json", new("id-a", 1, "one"));
        var bPath = await ctx.WriteRecordAsync("b.json", new("id-b", 1, "two"));

        ctx.RequestDirectoryReconcile();
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        File.Delete(bPath);

        ctx.RequestDirectoryReconcile();
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using var scope = await ctx.Index.EnterSharedScopeAsync();
        Assert.Single(scope.Records);
        Assert.Single(scope.Files);
        Assert.True(scope.Records.ContainsKey("id-a"));
        Assert.True(scope.Files.ContainsKey("a.json"));
        Assert.False(scope.Files.ContainsKey("b.json"));
    }

    [Fact]
    public async Task RequestDirectoryReconcile_WhenDirectoryHasMixedFiles_BuildsIndexAndMigratesLegacy()
    {
        await using var ctx = await ReconcilerTestContext.CreateAsync();
        await ctx.WriteRecordAsync("a.json", new("id-a", 1, "one"));
        var legacyPath = await ctx.WriteLegacyRecordAsync("legacy.json", new("id-legacy", 0, "old"));
        await File.WriteAllTextAsync(Path.Combine(ctx.TablePath, "broken.json"), "{ not json");

        ctx.RequestDirectoryReconcile();
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using (var scope = await ctx.Index.EnterSharedScopeAsync())
        {
            Assert.Equal(2, scope.Records.Count);
            Assert.Equal(2, scope.Files.Count);
            Assert.True(scope.Records.ContainsKey("id-a"));
            Assert.True(scope.Records.ContainsKey("id-legacy"));
            Assert.True(scope.Files.ContainsKey("a.json"));
            Assert.True(scope.Files.ContainsKey("legacy.json"));
            Assert.False(scope.Files.ContainsKey("broken.json"));
            Assert.Equal("migrated-old", scope.Files["legacy.json"].Projection);
        }

        var persistedLegacy = await File.ReadAllTextAsync(legacyPath);
        var persistedLegacyRecord = JsonSerializer.Deserialize(persistedLegacy, TestsJsonContext.Default.TestRecord);
        Assert.Equal(new TestRecord("id-legacy", 1, "migrated-old"), persistedLegacyRecord);
        Assert.DoesNotContain("LegacyValue", persistedLegacy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestDirectoryReconcile_WhenTwoFilesShareSameId_KeepsBothFilesAndSelectsLatestAsCurrent()
    {
        await using var ctx = await ReconcilerTestContext.CreateAsync();
        var aPath = await ctx.WriteRecordAsync("a.json", new("shared-id", 1, "one"));
        var bPath = await ctx.WriteRecordAsync("b.json", new("shared-id", 1, "two"));
        File.SetLastWriteTimeUtc(aPath, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(bPath, new DateTime(2024, 1, 1, 0, 0, 1, DateTimeKind.Utc));

        ctx.RequestDirectoryReconcile();
        await ctx.Scheduler.RunAllAsync();
        AssertNoRetry(ctx);

        using var scope = await ctx.Index.EnterSharedScopeAsync();
        var record = Assert.Single(scope.Records).Value;
        Assert.Equal("shared-id", record.Id);
        Assert.Equal(2, record.Files.Count);
        Assert.Equal("b.json", record.CurrentFileName);
        Assert.True(scope.Files.ContainsKey("a.json"));
        Assert.True(scope.Files.ContainsKey("b.json"));
        Assert.Equal("two", new ProjectionIndexView<string, string>(ctx.Index.Records)["shared-id"]);
    }

    private static void AssertNoRetry(ReconcilerTestContext ctx) => Assert.Equal(0, ctx.Scheduler.PendingCount);
}
