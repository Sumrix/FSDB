using System;
using System.Collections.Generic;

namespace FSDB.Model.Building;

public class TableOptions<TKey, TRecord>
{
    public string? Name { get; set; }
    public IComparer<TKey>? KeyComparer { get; set; }
    public IEqualityComparer<TKey>? KeyEqualityComparer { get; set; }
    public Func<TRecord, IEnumerable<string>>? FileNameGenerator { get; set; }
}
