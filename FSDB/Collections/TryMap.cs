using System.Diagnostics.CodeAnalysis;

namespace FSDB.Collections;

public delegate bool TryMap<in TSource, TValue>(
    TSource source,
    [MaybeNullWhen(false)] out TValue value);
