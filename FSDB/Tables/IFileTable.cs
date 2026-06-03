using System.Threading;
using System.Threading.Tasks;

namespace FSDB.Tables;

/// <summary>
/// Represents a file-aware table API that reports expected file access and parsing failures as operation results.
/// </summary>
public interface IFileTable<in TKey, TRecord>
{
    /// <summary>
    /// Reads a record by id and returns file-aware operation details for expected access or parsing failures.
    /// </summary>
    Task<ReadResult<TRecord>> ReadAsync(TKey id, CancellationToken ct = default);

    /// <summary>
    /// Fully replaces the stored state of a record and returns file-aware operation details for expected access failures.
    /// </summary>
    Task<OperationResult> WriteAsync(TRecord record, CancellationToken ct = default);

    /// <summary>
    /// Removes a record and returns file-aware operation details for expected access failures.
    /// </summary>
    Task<OperationResult> RemoveAsync(TKey id, CancellationToken ct = default);
}
