using System.Diagnostics.CodeAnalysis;

namespace FSDB.FileStorage;

/// <summary>
/// Represents the outcome of a file delete operation.
/// </summary>
public readonly record struct FileDeleteResult(
    FileError? Error = null) : IFileOperationResult
{
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;
}
