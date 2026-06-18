using System;
using System.Threading;
using Nito.Disposables;

namespace FSDB.Infrastructure.Concurrency;

public static class ReaderWriterLockSlimExtensions
{
    public static IDisposable ReadLock(this ReaderWriterLockSlim rw)
    {
        rw.EnterReadLock();
        return Disposable.Create(rw.ExitReadLock);
    }

    public static IDisposable WriteLock(this ReaderWriterLockSlim rw)
    {
        rw.EnterWriteLock();
        return Disposable.Create(rw.ExitWriteLock);
    }
}