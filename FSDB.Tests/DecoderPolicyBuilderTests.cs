using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Encoding;
using FSDB.Infrastructure.Exceptions;
using FSDB.Model;
using FSDB.Tests.TestSupport;

namespace FSDB.Tests;

public class DecoderPolicyBuilderTests
{
    [Fact]
    public async Task WithoutVersioning_WithCustomJsonTypeInfo_UsesItForDecode()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        var codec = new RecordCodec<string, SnakeCaseRecord>(
            new DecoderPolicyBuilder().WithoutVersioning<SnakeCaseRecord>(options));
        var record = new SnakeCaseRecord("id-1", "value-1");

        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(record, options));
        var decoded = await codec.DecodeAsync(stream, CancellationToken.None);

        Assert.False(decoded.Upgraded);
        Assert.Null(decoded.SourceSchemaVersion);
        Assert.Null(decoded.TargetSchemaVersion);
        Assert.Equal(record, decoded.Record);
    }

    [Fact]
    public async Task Build_WithLegacySchema_ReturnsSourceAndTargetSchemaVersions()
    {
        var codec = new RecordCodec<string, TestRecord>(
            new DecoderPolicyBuilder()
                .StartWith<LegacyMigrationRecord>(0)
                .UpgradeTo(1, legacy => new TestRecord(legacy.Id, 1, legacy.Value), TestsJsonContext.Default.TestRecord)
                .Build());

        var record = new LegacyMigrationRecord("id-1", 0, "value-1");
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(record));

        var decoded = await codec.DecodeAsync(stream, CancellationToken.None);

        Assert.True(decoded.Upgraded);
        Assert.Equal(0, decoded.SourceSchemaVersion);
        Assert.Equal(1, decoded.TargetSchemaVersion);
        Assert.Equal(new TestRecord("id-1", 1, "value-1"), decoded.Record);
    }

    [Fact]
    public async Task Build_WithUpgradeReturningUnexpectedSchemaVersion_ThrowsRecordConversionException()
    {
        var codec = new RecordCodec<string, TestRecord>(
            new DecoderPolicyBuilder()
                .StartWith<LegacyMigrationRecord>(0)
                .UpgradeTo(1, legacy => new TestRecord(legacy.Id, 0, legacy.Value), TestsJsonContext.Default.TestRecord)
                .Build());

        var record = new LegacyMigrationRecord("id-1", 0, "value-1");
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(record));

        var exception = await Assert.ThrowsAsync<RecordConversionException>(
            () => codec.DecodeAsync(stream, CancellationToken.None));

        Assert.Contains("expected=1", exception.Message);
        Assert.Contains("actual=0", exception.Message);
    }

    private sealed record SnakeCaseRecord(string Id, string DisplayValue) : IRecord<string>;

    private sealed record LegacyMigrationRecord(string Id, int SchemaVersion, string Value) : IVersionedRecord;
}
