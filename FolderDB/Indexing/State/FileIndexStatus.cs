namespace FolderDB.Indexing.State;

/// <summary>
/// Describes how FolderDB currently understands an indexed file.
/// </summary>
public enum FileIndexStatus
{
    Reserved,
    Committed
}
