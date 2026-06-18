using System.Threading;
using System.Threading.Tasks;
using FSDB.Encoding;

namespace FSDB.FileStorage;

public sealed class RecordStore<TKey, TRecord>(
    IRecordCodec<TKey, TRecord> codec,
    IFileStore store)
{
    public Task<FileReadResult<RecordDecodeResult<TRecord>>> ReadAsync(string path, CancellationToken ct)
        => store.ReadAsync(path, stream => codec.DecodeAsync(stream, ct), ct);

    public Task<FileWriteResult> WriteAsync(string filePath, TRecord record, CancellationToken ct)
        => store.WriteAsync(filePath, stream => codec.EncodeAsync(stream, record, ct), ct);

    public Task<FileDeleteResult> DeleteAsync(string filePath, CancellationToken ct)
        => store.DeleteAsync(filePath, ct);
}
