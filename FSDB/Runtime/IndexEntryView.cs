using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FSDB.Indexing.State;
using FSDB.Model;

namespace FSDB.Runtime;

// TODO: Remove
public class IndexEntryView<TKey, TProjection>(
    IReadOnlyDictionary<TKey, IReadOnlyRecordIndexState<TKey, TProjection>> records)
    : IReadOnlyDictionary<TKey, IndexEntry<TProjection>>
    where TKey : notnull
{
    public IEnumerator<KeyValuePair<TKey, IndexEntry<TProjection>>> GetEnumerator()
    {
        foreach (var (key, record) in records)
        {
            if (TryCreateEntry(record, out var entry))
            {
                yield return new(key, entry);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => this.Count();

    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out IndexEntry<TProjection> value)
    {
        if (records.TryGetValue(key, out var record) && TryCreateEntry(record, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    public IndexEntry<TProjection> this[TKey key] => TryGetValue(key, out var value)
        ? value
        : throw new KeyNotFoundException();

    public IEnumerable<TKey> Keys => this.Select(kv => kv.Key);

    public IEnumerable<IndexEntry<TProjection>> Values => this.Select(kv => kv.Value);
    
    private static bool TryCreateEntry(
        IReadOnlyRecordIndexState<TKey, TProjection> record,
        [MaybeNullWhen(false)] out IndexEntry<TProjection> entry)
    {
        var fileState = record.GetCurrentFileState();
        if (fileState.Status != FileIndexStatus.Reserved)
        {
            entry = new(fileState.Projection, fileState.ErrorInfo, record.CurrentFileName);
            return true;
        }

        entry = null;
        return false;
    }
}
