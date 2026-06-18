using System;

namespace FSDB.FileStorage;

/// <summary>
/// A file processing error.
/// </summary>
/// <param name="Reason">
/// The reason FSDB classified the file error.
/// </param>
/// <param name="Persistence">
/// Whether FSDB classified the file error as temporary or persistent.
/// </param>
/// <param name="Exception">
/// The exception that caused the file error.
/// </param>
public sealed record FileError(
    FileErrorReason Reason,
    FileErrorPersistence Persistence,
    Exception Exception)
{
    public FileErrorInfo ToErrorInfo() => FileErrorInfo.Create(Reason, Persistence, Exception);
}
