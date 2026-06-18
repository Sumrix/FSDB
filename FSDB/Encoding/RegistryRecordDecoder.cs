using System.Collections.Generic;
using System.Text.Json;
using FSDB.Infrastructure.Exceptions;
using FSDB.Model;

namespace FSDB.Encoding;

public class RegistryRecordDecoder<TRecord>(IReadOnlyDictionary<int, IRecordDecoder<TRecord>> decoders)
    : ISchemaAwareRecordDecoder<TRecord>
    where TRecord : class, IVersionedRecord
{
    public RecordDecodeResult<TRecord> Decode(JsonDocument document)
    {
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty(nameof(IVersionedRecord.SchemaVersion), out var schemaVersionElement) &&
            schemaVersionElement.ValueKind == JsonValueKind.Number &&
            schemaVersionElement.TryGetInt32(out int sourceSchemaVersion))
        {
            var decoder = GetDecoder(sourceSchemaVersion);
            var record = decoder.Decode(document);
            return new RecordDecodeResult<TRecord>(
                sourceSchemaVersion != record.SchemaVersion,
                sourceSchemaVersion,
                record.SchemaVersion,
                record);
        }

        throw new RecordConversionException(
            $"Record does not contain a valid {nameof(IVersionedRecord.SchemaVersion)} property.");
    }

    private IRecordDecoder<TRecord> GetDecoder(int version)
    {
        if (!decoders.TryGetValue(version, out var decoder))
        {
            throw new RecordConversionException(
                $"Unsupported schema version v{version} for {typeof(TRecord).Name}.");
        }

        return decoder;
    }
}
