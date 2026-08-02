using System.Text.Json;

namespace FolderDB.Encoding;

public interface IRecordDecoder<out TRecord>
{
    TRecord Decode(JsonDocument document);
}
