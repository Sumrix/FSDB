using System.Threading;
using System.Threading.Tasks;

namespace FSDB.Model;

/// <summary>
/// Represents a strict table-like API over file-backed records.
/// </summary>
public interface ITable<in TKey, TRecord>
{
    /// <summary>
    /// Gets a record by id, or returns null when no record is present.
    /// </summary>
    /// <exception cref="System.Exception">Throws when the record exists but cannot be read or decoded.</exception>
    Task<TRecord?> GetAsync(TKey id, CancellationToken ct = default);

    /// <summary>
    /// Fully replaces the stored state of a record.
    /// </summary>
    /// <exception cref="System.Exception">Throws when the target file cannot be written.</exception>
    Task UpsertAsync(TRecord record, CancellationToken ct = default);

    /// <summary>
    /// Deletes a record and all known files that belong to it.
    /// </summary>
    /// <exception cref="System.Exception">Throws when a known file cannot be deleted.</exception>
    Task DeleteAsync(TKey id, CancellationToken ct = default);
}
