using System;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Files;
using FSDB.Scheduling;
using Microsoft.Extensions.Logging;

namespace FSDB.Tables;

public interface ITableDefinition
{
    string Name { get; }
    Type RecordType { get; }
    
    Task<ITableEngine> StartEngineAsync(
        string tablePath,
        string indexFilePath,
        IFileStore fileStore,
        IWorkScheduler<string> workScheduler,
        DatabaseOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken ct = default);
}
