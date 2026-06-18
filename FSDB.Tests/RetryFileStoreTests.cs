using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FSDB.FileStorage;
using FSDB.Model;

namespace FSDB.Tests;

public class RetryFileStoreTests
{
    [Fact]
    public async Task WriteAsync_WhenUserOptionsMutateAfterConstruction_UsesConstructorSnapshot()
    {
        var inner = new TransientWriteFileStore(failuresBeforeSuccess: 1);
        var writeOptions = new RetryFileStoreOperationOptions
        {
            MaxAttempts = 2,
            Delay = TimeSpan.Zero
        };
        var retryStore = new RetryFileStore(
            inner,
            new RetryFileStoreOptions { Write = writeOptions });

        writeOptions.MaxAttempts = 1;

        var result = await retryStore.WriteAsync("record.json", _ => Task.CompletedTask, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.WriteAttempts);
    }

    [Fact]
    public async Task WriteAsync_WhenOperationOptionsAreNull_UsesDefaultOperationOptions()
    {
        var inner = new TransientWriteFileStore(failuresBeforeSuccess: 4);
        var retryStore = new RetryFileStore(
            inner,
            new RetryFileStoreOptions { Write = null! });

        var result = await retryStore.WriteAsync("record.json", _ => Task.CompletedTask, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, inner.WriteAttempts);
    }

    [Fact]
    public async Task WriteAsync_WhenRetriesAreExhausted_ReturnsLastException()
    {
        var inner = new TransientWriteFileStore(failuresBeforeSuccess: 10);
        var retryStore = new RetryFileStore(
            inner,
            new RetryFileStoreOptions
            {
                Write = new RetryFileStoreOperationOptions
                {
                    MaxAttempts = 2,
                    Delay = TimeSpan.Zero
                }
            });

        var result = await retryStore.WriteAsync("record.json", _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(FileErrorPersistence.Transient, result.Error?.Persistence);
        Assert.False(result.IsSuccess);
        Assert.Equal(2, inner.WriteAttempts);
        var exception = Assert.IsType<IOException>(result.Error?.Exception);
        Assert.Equal("transient write attempt 2", exception.Message);
    }

    private sealed class TransientWriteFileStore(int failuresBeforeSuccess) : IFileStore
    {
        public int WriteAttempts { get; private set; }

        public Task<FileWriteResult> WriteAsync(
            string path,
            Func<Stream, Task> writeAction,
            CancellationToken ct)
        {
            WriteAttempts++;
            return Task.FromResult(
                WriteAttempts <= failuresBeforeSuccess
                    ? new FileWriteResult(
                        null,
                        new FileError(
                            FileErrorReason.Unavailable,
                            FileErrorPersistence.Transient,
                            new IOException($"transient write attempt {WriteAttempts}")))
                    : new FileWriteResult(
                        new FileFingerprint(DateTime.UtcNow, 1, Exists: true)));
        }

        public Task<FileReadResult<T>> ReadAsync<T>(string path, Func<Stream, Task<T>> parseAction, CancellationToken ct)
        {
            return Task.FromResult(new FileReadResult<T>(
                default,
                null,
                new FileError(
                    FileErrorReason.Unavailable,
                    FileErrorPersistence.Persistent,
                    new IOException("read failed"))));
        }

        public Task<FileDeleteResult> DeleteAsync(string path, CancellationToken ct)
        {
            return Task.FromResult(new FileDeleteResult(
                new FileError(
                    FileErrorReason.Unavailable,
                    FileErrorPersistence.Persistent,
                    new IOException("delete failed"))));
        }

        public FileFingerprint GetFileFingerprint(string path)
        {
            return default;
        }
    }
}
