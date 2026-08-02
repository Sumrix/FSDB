using System;
using FolderDB.Infrastructure.Helpers;

namespace FolderDB.Indexing.Scopes;

public sealed record RecordScopePair<TKey, TRecord, TProjection>(
    RecordScope<TKey, TRecord, TProjection>? First,
    RecordScope<TKey, TRecord, TProjection>? Second)
    : IDisposable
    where TKey : notnull
{
    public void Dispose()
    {
        DisposeHelper.SafeDispose(First);
        if (!ReferenceEquals(First, Second))
        {
            DisposeHelper.SafeDispose(Second);
        }
    }
}
