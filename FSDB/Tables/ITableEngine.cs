using System;
using System.Threading;
using System.Threading.Tasks;

namespace FSDB.Tables;

public interface ITableEngine : IAsyncDisposable
{
    void RequestDirectoryReconcile();
    Task FlushAsync(CancellationToken ct = default);
}
