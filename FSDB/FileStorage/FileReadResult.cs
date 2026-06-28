using System.Diagnostics.CodeAnalysis;

namespace FSDB.FileStorage;

/// <summary>
/// Represents the outcome of a file read operation.
/// </summary>
public readonly record struct FileReadResult<T>(
    T? Value,
    FileFingerprint Fingerprint,
    FileError? Error = null) : IFileOperationResult
{
    public bool IsFileAccessSuccessful => Error?.Reason != FileErrorReason.Unavailable;

    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsSuccess => Error is null && Fingerprint.Exists;
}
