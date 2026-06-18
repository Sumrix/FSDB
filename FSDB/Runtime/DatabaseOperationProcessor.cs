using System;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Model;

namespace FSDB.Runtime;

internal class DatabaseOperationProcessor<TKey, TRecord, TProjection>(
    FileOperationProcessor<TKey, TRecord, TProjection> fileOperationProcessor)
    where TRecord : class, IRecord<TKey>
    where TKey : notnull
{
    public async Task<TRecord?> GetAsync(TKey id, CancellationToken ct = default)
    {
        var result = await fileOperationProcessor.ReadAsync(id, ct);
        if (!result.IsSuccess)
        {
            ThrowOperationFailed("get", id, result);
        }

        return result.Record;
    }

    public async Task UpsertAsync(TRecord record, CancellationToken ct = default)
    {
        var result = await fileOperationProcessor.WriteAsync(record, ct);
        if (!result.IsSuccess)
        {
            ThrowOperationFailed("upsert", record.Id, result);
        }
    }

    public async Task DeleteAsync(TKey id, CancellationToken ct = default)
    {
        var result = await fileOperationProcessor.RemoveAsync(id, ct);
        if (!result.IsSuccess)
        {
            ThrowOperationFailed("delete", id, result);
        }
    }

    private static void ThrowOperationFailed(string operation, TKey id, OperationResult result)
    {
        if (result.Error?.Exception is not null)
        {
            throw result.Error.Exception;
        }

        throw new InvalidOperationException(
            $"Failed to {operation} record id '{id}'. Error: {result.ErrorReason}.");
    }
}
