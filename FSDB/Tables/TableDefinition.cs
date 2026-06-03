using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Files;
using FSDB.Index;
using FSDB.Index.Persistence;
using FSDB.Migration;
using FSDB.Scheduling;
using FSDB.Tables.Building;
using Microsoft.Extensions.Logging;

namespace FSDB.Tables;

public class TableDefinition<TKey, TRecord, TProjection>: ITableDefinition
    where TRecord : class, IRecord<TKey>
    where TKey : notnull
{
    public required string Name { get; init; }
    public Type RecordType => typeof(TRecord);
    public required IComparer<TKey> KeyComparer { get; init; }
    public required IEqualityComparer<TKey> KeyEqualityComparer { get; init; }
    public required Func<TRecord, IEnumerable<string>> FileNameGenerator { get; init; }
    public required Func<TRecord, TProjection> CreateProjection { get; init; }
    public required Func<RecordCodecContext, IRecordCodec<TKey, TRecord>> RecordCodecFactory { get; init; }
    public required Func<TableIndexPersistenceContext<TKey, TRecord, TProjection>, ITableIndexPersistence<TKey, TProjection>>? IndexPersistenceFactory { get; init; }
    public required Func<RecordScopedIndexEngineContext<TKey, TRecord, TProjection>, Task<IRecordScopedIndexEngine<TKey, TRecord, TProjection>>>? IndexEngineFactory { get; init; }

    public async Task<ITableEngine> StartEngineAsync(
        string tablePath,
        string indexFilePath,
        IFileStore fileStore,
        IWorkScheduler<string> workScheduler,
        DatabaseOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken ct = default)
    {
        return await TableEngine<TKey, TRecord, TProjection>.StartAsync(
            tablePath,
            indexFilePath,
            this,
            fileStore,
            workScheduler,
            options,
            loggerFactory,
            ct);
    }
}
