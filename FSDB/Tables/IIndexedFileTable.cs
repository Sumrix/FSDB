using System.Collections.Generic;

namespace FSDB.Tables;

/// <summary>
/// Represents a file-aware table API with a status-rich record index.
/// </summary>
public interface IIndexedFileTable<TKey, TRecord, TProjection> : IFileTable<TKey, TRecord>
{
    /// <summary>
    /// Gets record index entries, including records whose files are currently unavailable or invalid.
    /// </summary>
    IReadOnlyDictionary<TKey, IndexEntry<TProjection>> Index { get; }
}
