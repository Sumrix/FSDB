namespace FSDB.Files;

public interface IFileOperationResult
{
    FileError? Error { get; }
}
