using System.Diagnostics.CodeAnalysis;
using FSDB.FileStorage;

namespace FSDB.Model;

/// <summary>
/// Describes the result of a file-aware table operation.
/// </summary>
/// <param name="Error">
/// The file error observed by the operation, or <see langword="null"/> when no file error occurred.
/// </param>
/// <param name="FileName">
/// The file name relative to the table directory, or <see langword="null"/> when no file is associated with the result.
/// </param>
public record OperationResult(
    FileError? Error = null,
    string? FileName = null)
{
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public FileErrorReason? ErrorReason => Error?.Reason;

    /// <summary>
    /// Gets a successful operation result.
    /// </summary>
    public static OperationResult Success { get; } = new();
}
