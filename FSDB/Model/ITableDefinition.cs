using System;
using System.Threading;
using System.Threading.Tasks;
using FSDB.FileStorage;
using FSDB.Retry;
using FSDB.Runtime;
using Microsoft.Extensions.Logging;

namespace FSDB.Model;

public interface ITableDefinition
{
    string Name { get; }
    Type RecordType { get; }
    
    Task<ITableEngine> StartEngineAsync(
        string tablePath,
        string indexFilePath,
        IFileStore fileStore,
        IRetryScheduler<string> retryScheduler,
        DatabaseOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken ct = default);
}
