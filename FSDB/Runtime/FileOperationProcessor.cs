using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FSDB.FileStorage;
using FSDB.Indexing;
using FSDB.Indexing.Scopes;
using FSDB.Indexing.State;
using FSDB.Infrastructure.Exceptions;
using FSDB.Infrastructure.Logging;
using FSDB.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSDB.Runtime;

internal class FileOperationProcessor<TKey, TRecord, TProjection>(
    string tablePath,
    TableContext<TKey, TRecord, TProjection> context,
    TableIndex<TKey, TRecord, TProjection> index,
    RecordStore<TKey, TRecord> store,
    int maxFileNameReserveAttempts,
    Action<string> requestFileReconcile,
    ILogger<FileOperationProcessor<TKey, TRecord, TProjection>>? logger)
    where TRecord : class, IRecord<TKey>
    where TKey : notnull
{
    private readonly int _maxFileNameReserveAttempts = Math.Max(1, maxFileNameReserveAttempts);
    private readonly ILogger<FileOperationProcessor<TKey, TRecord, TProjection>> _logger =
        logger ?? NullLogger<FileOperationProcessor<TKey, TRecord, TProjection>>.Instance;

    public async Task<ReadResult<TRecord>> ReadAsync(TKey id, CancellationToken ct = default)
    {
        using var _ = _logger.BeginMethodScope();
        using var sharedIndexScope = await index.EnterSharedScopeAsync(ct);
        using var recordScope = await sharedIndexScope.LockRecordAsync(id, ct);

        if (!recordScope.TryGetState(out var recordState))
        {
            _logger.LogTrace("Get miss in index: id={Id}", id);
            return new ReadResult<TRecord>(null);
        }

        var fileName = recordState.CurrentFileName;
        var filePath = Path.Combine(tablePath, fileName);
        var readResult = await store.ReadAsync(filePath, ct);

        if (readResult.Error != null)
        {
            _logger.LogWarning(
                "Get read failed, marked file error info: id={Id} file=\"{File}\" errorReason={ErrorReason} errorPersistence={ErrorPersistence}",
                id,
                fileName,
                readResult.Error.Reason,
                readResult.Error.Persistence);

            var fingerprint = readResult.Fingerprint;
            var errorUpsertResult = recordScope.Upsert(fileName, fingerprint, readResult.Error.ToErrorInfo());
            if (errorUpsertResult == IndexOperationResult.BlockedByAnotherId)
            {
                _logger.LogDebug(
                    "Get read error index update deferred to reconciler: id={Id} file=\"{File}\" errorReason={ErrorReason} errorPersistence={ErrorPersistence}",
                    id,
                    fileName,
                    readResult.Error.Reason,
                    readResult.Error.Persistence);
                requestFileReconcile(filePath);
            }
            return new ReadResult<TRecord>(null, readResult.Error, fileName);
        }

        var readFingerprint = readResult.Fingerprint;
        if (!readFingerprint.Exists)
        {
            _logger.LogDebug(
                "Get file missing, removed from index: id={Id} file=\"{File}\"",
                id,
                fileName);
            var deleteResult = recordScope.DeleteFile(fileName);
            if (deleteResult == IndexOperationResult.BlockedByAnotherId)
            {
                _logger.LogDebug(
                    "Get missing file index delete deferred to reconciler: id={Id} file=\"{File}\"",
                    id,
                    fileName);
                requestFileReconcile(filePath);
            }
            return new ReadResult<TRecord>(null, null, fileName);
        }

        var record = readResult.Value.Record;
        if (!context.KeyEqualityComparer.Equals(record.Id, id))
        {
            var deleteResult = recordScope.DeleteFile(fileName);
            _logger.LogDebug(
                "Get found file reassigned to another id, queued reconcile: expectedId={ExpectedId} actualId={ActualId} file=\"{File}\" indexDeleteResult={IndexDeleteResult}",
                id,
                record.Id,
                fileName,
                deleteResult);
            requestFileReconcile(filePath);
            return new ReadResult<TRecord>(null, null, fileName);
        }

        var upsertResult = recordScope.Upsert(fileName, readFingerprint, record);
        if (upsertResult == IndexOperationResult.BlockedByAnotherId)
        {
            _logger.LogDebug(
                "Get read index update deferred to reconciler: id={Id} file=\"{File}\" fingerprint=\"{Fingerprint}\"",
                id,
                fileName,
                readFingerprint);
            requestFileReconcile(filePath);
        }
        _logger.LogTrace("Get hit: id={Id} file=\"{File}\"", id, fileName);

        return new ReadResult<TRecord>(record, null, fileName);
    }

    public async Task<OperationResult> WriteAsync(TRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var _ = _logger.BeginMethodScope();
        using var sharedIndexScope = await index.EnterSharedScopeAsync(ct);
        using var recordScope = await sharedIndexScope.LockRecordAsync(record.Id, ct);

        if (!recordScope.TryGetState(out var recordState))
        {
            return await TryUpsertNewFileAsync(recordScope, record, ct);
        }

        var fileName = recordState.CurrentFileName;
        var currentPath = Path.Combine(tablePath, fileName);
        var writeResult = await store.WriteAsync(currentPath, record, ct);

        if (writeResult.Error != null)
        {
            _logger.LogWarning(
                "Upsert write failed: id={Id} file=\"{File}\" errorReason={ErrorReason} errorPersistence={ErrorPersistence}",
                record.Id,
                fileName,
                writeResult.Error.Reason,
                writeResult.Error.Persistence);
            return new OperationResult(writeResult.Error, fileName);
        }

        var upsertResult = recordScope.Upsert(fileName, writeResult.Fingerprint!.Value, record);
        if (upsertResult == IndexOperationResult.BlockedByAnotherId)
        {
            // Impossible scenario since the file is locked
            throw new InvalidOperationException($"Failed to upsert record id '{record.Id}' to existing file '{fileName}'.");
        }

        _logger.LogDebug("Upsert applied to current file: id={Id} file=\"{File}\"", record.Id, fileName);
        return new OperationResult(null, fileName);

    }

    public async Task<OperationResult> RemoveAsync(TKey id, CancellationToken ct = default)
    {
        using var _ = _logger.BeginMethodScope();
        using var sharedIndexScope = await index.EnterSharedScopeAsync(ct);
        using var recordScope = await sharedIndexScope.LockRecordAsync(id, ct);

        if (!recordScope.TryGetState(out var recordState))
        {
            _logger.LogTrace("Delete miss in index: id={Id}", id);
            return OperationResult.Success;
        }

        var fileNames = recordState.Files.Keys.ToArray();
        foreach (var fileName in fileNames)
        {
            ct.ThrowIfCancellationRequested();

            var filePath = Path.Combine(tablePath, fileName);
            var deleteResult = await store.DeleteAsync(filePath, ct);
            if (deleteResult.Error != null)
            {
                _logger.LogError(
                    "Delete failed: id={Id} file=\"{File}\" errorReason={ErrorReason} errorPersistence={ErrorPersistence}",
                    id,
                    fileName,
                    deleteResult.Error.Reason,
                    deleteResult.Error.Persistence);
                return new OperationResult(deleteResult.Error, fileName);
            }

            var deleteFileResult = recordScope.DeleteFile(fileName);
            if (deleteFileResult == IndexOperationResult.BlockedByAnotherId)
            {
                // Impossible scenario since the file is locked
                throw new InvalidOperationException($"Failed to delete file '{fileName}' for record id '{id}'.");
            }

            _logger.LogDebug(
                "Deleted file: id={Id} file=\"{File}\" indexDeleteResult={IndexDeleteResult}",
                id,
                fileName,
                deleteFileResult);
        }

        _logger.LogDebug("Delete applied: id={Id} files={Files}", id, fileNames.Length);
        return new OperationResult(null, fileNames[0]);
    }

    private async Task<OperationResult> TryUpsertNewFileAsync(
        RecordScope<TKey, TRecord, TProjection> recordScope,
        TRecord record,
        CancellationToken ct)
    {
        using var iterator = CreateFileNameIterator(record);
        for (var attempt = 0; attempt < _maxFileNameReserveAttempts; attempt++)
        {
            if (!MoveNextFileNameCandidate(record.Id, iterator))
            {
                throw new InvalidOperationException($"Unable to reserve a file name for record id '{record.Id}'.");
            }

            var reservedFileName = BuildFileName(record.Id, iterator.Current);
            if (!recordScope.TryReserveFileName(reservedFileName))
            {
                continue;
            }

            try
            {
                var reservedPath = Path.Combine(tablePath, reservedFileName);

                // This can overwrite a disk-only file with the same name, which is acceptable because
                // the file system is not lockable by FSDB and can change at any time.
                var writeResult = await store.WriteAsync(reservedPath, record, ct);
                if (writeResult.Error != null)
                {
                    recordScope.DeleteFile(reservedFileName);
                    _logger.LogWarning(
                        "Upsert write failed: id={Id} file=\"{File}\" errorReason={ErrorReason} errorPersistence={ErrorPersistence}",
                        record.Id,
                        reservedFileName,
                        writeResult.Error.Reason,
                        writeResult.Error.Persistence);
                    return new OperationResult(writeResult.Error, reservedFileName);
                }

                if (!recordScope.CommitReservedFileName(reservedFileName, writeResult.Fingerprint!.Value, record))
                {
                    // Impossible scenario since the file is reserved
                    throw new InvalidOperationException($"Failed to commit reserved file name for record id '{record.Id}'.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                recordScope.DeleteFile(reservedFileName);
                throw;
            }
            catch (Exception)
            {
                // Just in case of unexpected exceptions
                recordScope.DeleteFile(reservedFileName);
                throw;
            }

            _logger.LogDebug("Upsert applied to reserved file: id={Id} file=\"{File}\"", record.Id, reservedFileName);
            return new OperationResult(null, reservedFileName);
        }

        throw new InvalidOperationException(
            $"Unable to reserve a file name for record id '{record.Id}' after {_maxFileNameReserveAttempts} attempts.");
    }

    private IEnumerator<string> CreateFileNameIterator(TRecord record)
    {
        try
        {
            return context.FileNameGenerator(record).GetEnumerator();
        }
        catch (Exception ex)
        {
            throw new FileNameGenerationException(
                $"Failed to start file-name generation for record id '{record.Id}'.", ex);
        }
    }

    private static bool MoveNextFileNameCandidate(TKey id, IEnumerator<string> iterator)
    {
        try
        {
            return iterator.MoveNext();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileNameGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FileNameGenerationException(
                $"Failed to generate file name for record id '{id}'.", ex);
        }
    }

    private static string BuildFileName(TKey id, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new FileNameGenerationException(
                $"File-name generator produced an empty candidate for record id '{id}'.");
        }

        if (candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new FileNameGenerationException(
                $"File-name generator produced a candidate with invalid file-name characters for record id '{id}'.");
        }

        const string extension = ".json";
        const int maxFileNameLength = 255;
        if (candidate.Length > maxFileNameLength - extension.Length)
        {
            throw new FileNameGenerationException(
                $"File-name generator produced a candidate that is too long for record id '{id}'.");
        }

        return candidate + extension;
    }
}
