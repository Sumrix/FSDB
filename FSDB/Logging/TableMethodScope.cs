using System;
using System.Collections;
using System.Collections.Generic;

namespace FSDB.Logging;

internal sealed class TableMethodScope(string table, string method) : IReadOnlyList<KeyValuePair<string, object?>>
{
    private const string _tableKey = "Table";
    private const string _methodKey = "Method";

    public int Count => 2;

    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new(_tableKey, table),
        1 => new(_methodKey, method),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public Enumerator GetEnumerator() => new(table, method);

    IEnumerator<KeyValuePair<string, object?>> IEnumerable<KeyValuePair<string, object?>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal sealed class Enumerator(string table, string method) : IEnumerator<KeyValuePair<string, object?>>
    {
        private int _index = -1;

        public KeyValuePair<string, object?> Current => _index switch
        {
            0 => new(_tableKey, table),
            1 => new(_methodKey, method),
            _ => default
        };

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_index >= 1)
            {
                return false;
            }

            _index++;
            return true;
        }

        public void Reset() => _index = -1;

        public void Dispose()
        {
        }
    }
}
