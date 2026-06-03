namespace FSDB.Index.State;

/// <summary>
/// Describes the result of a scoped index mutation.
/// </summary>
public enum IndexOperationResult
{
    Applied,

    NoChanges,

    BlockedByAnotherId
}
