using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Model;

namespace FSDB.Encoding;

public class RecordCodec<TKey, TRecord>(DecoderPolicy<TRecord> decoderPolicy)
    : IRecordCodec<TKey, TRecord>
    where TRecord : IRecord<TKey>
{
    public int? CurrentSchemaVersion => decoderPolicy.CurrentSchemaVersion;

    public async Task<RecordDecodeResult<TRecord>> DecodeAsync(Stream jsonStream, CancellationToken ct)
    {
        using var document = await JsonDocument.ParseAsync(jsonStream, cancellationToken: ct);
        return decoderPolicy.Decoder.Decode(document);
    }

    public async Task EncodeAsync(Stream jsonStream, TRecord record, CancellationToken ct)
    {
        await JsonSerializer.SerializeAsync(jsonStream, record, decoderPolicy.JsonTypeInfo, ct);
    }
}
