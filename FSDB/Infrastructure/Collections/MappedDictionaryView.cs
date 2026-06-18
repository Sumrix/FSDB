using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace FSDB.Infrastructure.Collections;

public class MappedDictionaryView<TKey, TSource, TValue>(
    IReadOnlyDictionary<TKey, TSource> source,
    TryMap<TSource, TValue> tryMap)
    : IReadOnlyDictionary<TKey, TValue>
{
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        foreach (var (key, sourceValue) in source)
        {
            if (tryMap(sourceValue, out var value))
            {
                yield return new(key, value);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => this.Count();

    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (source.TryGetValue(key, out var sourceValue) &&
            tryMap(sourceValue, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public TValue this[TKey key] => TryGetValue(key, out var value)
        ? value
        : throw new KeyNotFoundException();

    public IEnumerable<TKey> Keys => this.Select(kv => kv.Key);

    public IEnumerable<TValue> Values => this.Select(kv => kv.Value);
}