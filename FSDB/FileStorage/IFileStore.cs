using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FSDB.FileStorage;

/// <summary>
/// Provides safe low-level file operations.
/// Implementations are expected to convert file access failures into result values instead of throwing.
/// To change file access error classification, provide a custom implementation of this interface.
/// </summary>
public interface IFileStore
{
    Task<FileReadResult<T>> ReadAsync<T>(string path, Func<Stream, Task<T>> parseAction, CancellationToken ct);

    Task<FileWriteResult> WriteAsync(string path, Func<Stream, Task> writeAction, CancellationToken ct);

    Task<FileDeleteResult> DeleteAsync(string path, CancellationToken ct);

    FileFingerprint GetFileFingerprint(string path);
}
