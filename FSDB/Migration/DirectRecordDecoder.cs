using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FSDB.Exceptions;

namespace FSDB.Migration;

public class DirectRecordDecoder<TRecord>(JsonTypeInfo<TRecord> jsonTypeInfo) : ISchemaAwareRecordDecoder<TRecord>
{
    public RecordDecodeResult<TRecord> Decode(JsonDocument document)
    {
        var record = document.Deserialize(jsonTypeInfo)
            ?? throw new RecordConversionException($"Failed to deserialize JSON to {typeof(TRecord).Name}.");

        return new RecordDecodeResult<TRecord>(false, null, null, record);
    }
}
