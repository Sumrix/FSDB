using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace FSDB.Infrastructure.Collections;

public class CovariantReadOnlyDictionary<TKey, TSourceValue, TTargetValue>(
    IReadOnlyDictionary<TKey, TSourceValue> source)
    : IReadOnlyDictionary<TKey, TTargetValue>
    where TSourceValue : TTargetValue
{
    public int Count => source.Count;
    public IEnumerable<TKey> Keys => source.Keys;
    public IEnumerable<TTargetValue> Values => source.Values.Cast<TTargetValue>();
    public TTargetValue this[TKey key] => source[key];

    public bool ContainsKey(TKey key)
    {
        return source.ContainsKey(key);
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TTargetValue value)
    {
        if (source.TryGetValue(key, out var record))
        {
            value = record;
            return true;
        }

        value = default;
        return false;
    }

    public IEnumerator<KeyValuePair<TKey, TTargetValue>> GetEnumerator()
    {
        foreach (var (key, value) in source)
        {
            yield return new KeyValuePair<TKey, TTargetValue>(key, value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}