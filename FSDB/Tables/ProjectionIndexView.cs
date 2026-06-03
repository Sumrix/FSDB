using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FSDB.Index.State;

namespace FSDB.Tables;

public class ProjectionIndexView<TKey, TProjection>(
    IReadOnlyDictionary<TKey, IReadOnlyRecordIndexState<TKey, TProjection>> records)
    : IReadOnlyDictionary<TKey, TProjection>
    where TKey : notnull
{
    public IEnumerator<KeyValuePair<TKey, TProjection>> GetEnumerator()
    {
        foreach (var (key, record) in records)
        {
            if (TryGetProjection(record, out var projection))
            {
                yield return new(key, projection);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => this.Count();

    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TProjection value)
    {
        if (records.TryGetValue(key, out var record) && TryGetProjection(record, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public TProjection this[TKey key] => TryGetValue(key, out var value)
        ? value
        : throw new KeyNotFoundException();

    public IEnumerable<TKey> Keys => this.Select(kv => kv.Key);

    public IEnumerable<TProjection> Values => this.Select(kv => kv.Value);

    private static bool TryGetProjection(
        IReadOnlyRecordIndexState<TKey, TProjection> record,
        [MaybeNullWhen(false)] out TProjection projection)
    {
        var fileState = record.GetCurrentFileState();
        if (fileState.Status == FileIndexStatus.Committed && fileState.ErrorInfo is null)
        {
            projection = fileState.Projection!;
            return true;
        }

        projection = default;
        return false;
    }
}
