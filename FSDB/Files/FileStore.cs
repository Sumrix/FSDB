using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSDB.Files;

public sealed class FileStore(ILogger<FileStore>? logger = null) : IFileStore
{
    private readonly ILogger<FileStore> _logger = logger ?? NullLogger<FileStore>.Instance;
    
    public async Task<FileReadResult<T>> ReadAsync<T>(string path, Func<Stream, Task<T>> parseAction, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var _ = _logger.BeginMethodScope();
        try
        {
            await using var fs = CreateReadStream(path);
            ct.ThrowIfCancellationRequested();

            T? result;
            FileError? error = null;
            try
            {
                result = await parseAction(fs);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                result = default;
                error = new FileError(FileErrorReason.Invalid, FileErrorPersistence.Persistent, e);
                _logger.LogWarning(e, "File parsing failed: path=\"{Path}\"", path);
            }

            var fingerprint = GetFileFingerprint(fs);
            return new FileReadResult<T>(result, fingerprint, error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new FileReadResult<T>(
                default,
                new FileFingerprint(null, null, Exists: false));
        }
        catch (IOException ex) when (IoErrorCodes.IsTransient(ex))
        {
            _logger.LogWarning(ex, "Read transient failure: path=\"{Path}\"", path);
            return new FileReadResult<T>(
                default,
                null,
                new FileError(FileErrorReason.Unavailable, FileErrorPersistence.Transient, ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Read failed: path=\"{Path}\"", path);
            return new FileReadResult<T>(
                default,
                null,
                new FileError(FileErrorReason.Unavailable, FileErrorPersistence.Persistent, ex));
        }
    }

    public async Task<FileWriteResult> WriteAsync(string path, Func<Stream, Task> writeAction, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var _ = _logger.BeginMethodScope();

        var tempPath = path + ".tmp";
        try
        {
            var directoryPath = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            await using (var fs = CreateWriteStream(tempPath))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await writeAction(fs);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Write action failed: tempPath=\"{TempPath}\"", tempPath);
                    return new FileWriteResult(
                        null,
                        new FileError(FileErrorReason.Invalid, FileErrorPersistence.Persistent, ex));
                }
            }

            var fp = GetFileFingerprint(tempPath);

            ct.ThrowIfCancellationRequested();

            try
            {
                File.Move(tempPath, path, overwrite: true);
            }
            // Windows can report a locked target file during File.Move replace as UnauthorizedAccessException.
            // FSDB treats this as transient because concurrent readers can temporarily block this commit step.
            catch (UnauthorizedAccessException ex) when (OperatingSystem.IsWindows())
            {
                _logger.LogWarning(ex, "Write replace transient failure: path=\"{Path}\" tempPath=\"{TempPath}\"", path, tempPath);
                return new FileWriteResult(
                    null,
                    new FileError(FileErrorReason.Unavailable, FileErrorPersistence.Transient, ex));
            }

            return new FileWriteResult(fp);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex) when (IoErrorCodes.IsTransient(ex))
        {
            _logger.LogWarning(ex, "Write transient failure: path=\"{Path}\"", path);
            return new FileWriteResult(
                null,
                new FileError(FileErrorReason.Unavailable, FileErrorPersistence.Transient, ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Write failed: path=\"{Path}\"", path);
            return new FileWriteResult(
                null,
                new FileError(FileErrorReason.Unavailable, FileErrorPersistence.Persistent, ex));
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Temporary file cleanup failed: path=\"{Path}\" tempPath=\"{TempPath}\"", path, tempPath);
            }
        }
    }

    public Task<FileDeleteResult> DeleteAsync(string path, CancellationToken ct)
    {
        using var _ = _logger.BeginMethodScope();

        ct.ThrowIfCancellationRequested();
        try
        {
            File.Delete(path);
            return Task.FromResult(new FileDeleteResult());
        }
        catch (IOException ex) when (IoErrorCodes.IsTransient(ex))
        {
            _logger.LogWarning(ex, "Delete transient failure: path=\"{Path}\"", path);
            return Task.FromResult(new FileDeleteResult(
                new FileError(FileErrorReason.Unavailable, FileErrorPersistence.Transient, ex)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete failed: path=\"{Path}\"", path);
            return Task.FromResult(new FileDeleteResult(
                new FileError(FileErrorReason.Unavailable, FileErrorPersistence.Persistent, ex)));
        }
    }

    public FileFingerprint GetFileFingerprint(string path)
    {
        var info = new FileInfo(path);
        return !info.Exists
            ? new FileFingerprint(null, null, false)
            : new FileFingerprint(info.LastWriteTimeUtc, info.Length, true);
    }

    private static FileFingerprint GetFileFingerprint(FileStream fs)
    {
        return new FileFingerprint(
            File.GetLastWriteTimeUtc(fs.SafeFileHandle),
            fs.Length,
            true);
    }

    private static FileStream CreateWriteStream(string path)
    {
        return new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
    }

    private static FileStream CreateReadStream(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);
    }
}
