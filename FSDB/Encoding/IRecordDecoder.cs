using System.Text.Json;

namespace FSDB.Encoding;

public interface IRecordDecoder<out TRecord>
{
    TRecord Decode(JsonDocument document);
}
