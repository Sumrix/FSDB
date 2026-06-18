using System;
using System.Collections.Generic;
using FSDB.Encoding;

namespace FSDB.Runtime;

public sealed class TableContext<TKey, TRecord, TProjection>
{
    public required string Name { get; init; }
    public required IComparer<TKey> KeyComparer { get; init; }
    public required IEqualityComparer<TKey> KeyEqualityComparer { get; init; }
    public required Func<TRecord, IEnumerable<string>> FileNameGenerator { get; init; }
    public required Func<TRecord, TProjection> CreateProjection { get; init; }
    public required IRecordCodec<TKey, TRecord> RecordCodec { get; init; }
}
