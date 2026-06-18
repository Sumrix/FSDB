using System;
using FSDB.Infrastructure.Helpers;

namespace FSDB.Indexing.Scopes;

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
