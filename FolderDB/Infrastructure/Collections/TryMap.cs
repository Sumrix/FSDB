using System.Diagnostics.CodeAnalysis;

namespace FolderDB.Infrastructure.Collections;

public delegate bool TryMap<in TSource, TValue>(
    TSource source,
    [MaybeNullWhen(false)] out TValue value);
