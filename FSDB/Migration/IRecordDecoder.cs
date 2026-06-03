using System.Text.Json;

namespace FSDB.Migration;

public interface IRecordDecoder<out TRecord>
{
    TRecord Decode(JsonDocument document);
}
