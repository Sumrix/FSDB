using System;
using System.Collections;
using System.Collections.Generic;

namespace FSDB.Logging;

internal sealed class MethodScope(string method) : IReadOnlyList<KeyValuePair<string, object?>>
{
    public string Method => method;

    public int Count => 1;

    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new("Method", method),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public Enumerator GetEnumerator() => new(method);

    IEnumerator<KeyValuePair<string, object?>> IEnumerable<KeyValuePair<string, object?>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal sealed class Enumerator(string method) : IEnumerator<KeyValuePair<string, object?>>
    {
        private int _index = -1;

        public KeyValuePair<string, object?> Current => _index switch
        {
            0 => new("Method", method),
            _ => default
        };

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_index >= 0)
            {
                return false;
            }

            _index = 0;
            return true;
        }

        public void Reset() => _index = -1;

        public void Dispose()
        {
        }
    }
}
