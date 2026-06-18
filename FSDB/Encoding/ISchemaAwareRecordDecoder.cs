using System.Text.Json;

namespace FSDB.Encoding;

public interface ISchemaAwareRecordDecoder<TRecord>
{
    RecordDecodeResult<TRecord> Decode(JsonDocument document);
}