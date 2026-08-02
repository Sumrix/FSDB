using System.Text.Json;

namespace FolderDB.Encoding;

public interface ISchemaAwareRecordDecoder<TRecord>
{
    RecordDecodeResult<TRecord> Decode(JsonDocument document);
}