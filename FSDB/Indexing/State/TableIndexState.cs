using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FSDB.Infrastructure.Helpers;

namespace FSDB.Indexing.State;

/// <summary>
/// Stores the mutable in-memory state of the table index.
/// </summary>
public class TableIndexState<TKey, TProjection>
    where TKey : notnull
{
    public ConcurrentDictionary<TKey, RecordIndexState<TKey, TProjection>> Records { get; }

    public ConcurrentDictionary<string, FileIndexState<TKey, TProjection>> Files { get; }

    public TableIndexState(IEqualityComparer<TKey> keyEqualityComparer)
    {
        ArgumentNullException.ThrowIfNull(keyEqualityComparer);

        Records = new(keyEqualityComparer);
        Files = new(PathHelper.OSDependedPathComparer);
    }
}
