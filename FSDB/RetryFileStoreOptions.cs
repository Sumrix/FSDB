using FSDB.Files;

namespace FSDB;

public sealed class RetryFileStoreOptions
{
    internal static readonly RetryFileStoreOptions Default = new();

    public RetryFileStoreOperationOptions Read { get; set; } = RetryFileStoreOperationOptions.CreateReadDefaults();
    public RetryFileStoreOperationOptions Write { get; set; } = RetryFileStoreOperationOptions.CreateWriteDefaults();
    public RetryFileStoreOperationOptions Delete { get; set; } = RetryFileStoreOperationOptions.CreateWriteDefaults();

    internal RetryFileStoreOptions CopyNormalized()
    {
        return new RetryFileStoreOptions
        {
            Read = Read?.CopyNormalized() ?? Default.Read,
            Write = Write?.CopyNormalized() ?? Default.Write,
            Delete = Delete?.CopyNormalized() ?? Default.Delete
        };
    }
}
