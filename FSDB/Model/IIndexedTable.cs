using System.Collections.Generic;

namespace FSDB.Model;

/// <summary>
/// Represents a strict table-like API with a committed projection index.
/// </summary>
public interface IIndexedTable<TKey, TRecord, TProjection> : ITable<TKey, TRecord>
{
    /// <summary>
    /// Gets the committed projection view. Unavailable, invalid, and reserved files are excluded.
    /// </summary>
    IReadOnlyDictionary<TKey, TProjection> Index { get; }
}
