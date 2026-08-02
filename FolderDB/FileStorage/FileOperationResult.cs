namespace FolderDB.FileStorage;

public interface IFileOperationResult
{
    FileError? Error { get; }
}
