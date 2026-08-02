namespace FolderDB.FileStorage;

/// <summary>
/// Specifies why FolderDB classified a file operation as failed.
/// </summary>
public enum FileErrorReason
{
    /// <summary>
    /// FolderDB could not read, write, delete, or otherwise access the file.
    /// </summary>
    Unavailable,

    /// <summary>
    /// FolderDB accessed the file, but the content could not be decoded or written as a valid record.
    /// </summary>
    Invalid
}
