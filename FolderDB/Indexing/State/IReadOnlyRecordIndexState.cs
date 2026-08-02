using System.Collections.Generic;

namespace FolderDB.Indexing.State;

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
