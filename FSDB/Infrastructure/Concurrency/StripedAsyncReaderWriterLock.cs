using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace FSDB.Infrastructure.Concurrency;

/// <summary>
/// Striped async reader/writer lock keyed by a value.
/// </summary>
public sealed class StripedAsyncReaderWriterLock<TKey>(int stripeCountPow2, IEqualityComparer<TKey>? comparer = null)
    : StripedBase<TKey, AsyncReaderWriterLock>(stripeCountPow2, static () => new AsyncReaderWriterLock(), comparer)
    where TKey : notnull
{
    /// <summary>
    /// Acquires a reader lock for the stripe corresponding to <paramref name="key"/>.
    /// </summary>
    /// <returns>A disposable that releases the lock when disposed.</returns>
    public Task<IDisposable> ReaderLockAsync(TKey key, CancellationToken cancellationToken = default)
        => GetStripe(key).ReaderLockAsync(cancellationToken);

    /// <summary>
    /// Acquires a writer lock for the stripe corresponding to <paramref name="key"/>.
    /// </summary>
    /// <returns>A disposable that releases the lock when disposed.</returns>
    public Task<IDisposable> WriterLockAsync(TKey key, CancellationToken cancellationToken = default)
        => GetStripe(key).WriterLockAsync(cancellationToken);
}