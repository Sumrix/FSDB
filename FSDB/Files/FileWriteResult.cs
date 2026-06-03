using System.Diagnostics.CodeAnalysis;

namespace FSDB.Files;

/// <summary>
/// Represents the outcome of a file write operation.
/// </summary>
public readonly record struct FileWriteResult(
    FileFingerprint? Fingerprint,
    FileError? Error = null) : IFileOperationResult
{
    public bool IsFileAccessSuccessful => Error?.Reason != FileErrorReason.Unavailable;

    [MemberNotNullWhen(true, nameof(Fingerprint))]
    public bool IsSuccess => Error is null;
}
