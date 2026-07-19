using System.Collections.Generic;

namespace FSDB.Indexing.State;

/// <summary>
/// Exposes read-only information about an indexed record.
/// </summary>
public interface IReadOnlyRecordIndexState<TKey, TProjection>
{
    TKey Id { get; }

    string CurrentFileName { get; }

    IReadOnlyDictionary<string, IReadOnlyFileIndexState<TKey, TProjection>> Files { get; }

    IReadOnlyFileIndexState<TKey, TProjection> GetCurrentFileState();
}
