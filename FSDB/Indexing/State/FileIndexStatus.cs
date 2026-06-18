namespace FSDB.Indexing.State;

/// <summary>
/// Describes how FSDB currently understands an indexed file.
/// </summary>
public enum FileIndexStatus
{
    Reserved,
    Committed
}
