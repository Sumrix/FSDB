namespace FSDB.Files;

/// <summary>
/// Specifies why FSDB classified a file operation as failed.
/// </summary>
public enum FileErrorReason
{
    /// <summary>
    /// FSDB could not read, write, delete, or otherwise access the file.
    /// </summary>
    Unavailable,

    /// <summary>
    /// FSDB accessed the file, but the content could not be decoded or written as a valid record.
    /// </summary>
    Invalid
}
