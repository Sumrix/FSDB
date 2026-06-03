namespace FSDB.Files;

/// <summary>
/// Specifies whether FSDB classified a file error as temporary or persistent.
/// </summary>
public enum FileErrorPersistence
{
    /// <summary>
    /// The error may disappear without changing the file content or operation input.
    /// </summary>
    Transient,

    /// <summary>
    /// The error is expected to persist until the file, permissions, or operation input changes.
    /// </summary>
    Persistent
}
