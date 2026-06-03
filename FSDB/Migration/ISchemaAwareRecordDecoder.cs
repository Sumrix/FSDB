using System.Text.Json;

namespace FSDB.Migration;

public interface ISchemaAwareRecordDecoder<TRecord>
{
    RecordDecodeResult<TRecord> Decode(JsonDocument document);
}