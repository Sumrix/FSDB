namespace FSDB.FileStorage;

public interface IFileOperationResult
{
    FileError? Error { get; }
}
