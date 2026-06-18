using System;

namespace FSDB.FileStorage;

/// <summary>
/// Contains information about a file processing error.
/// </summary>
/// <param name="Reason">
/// The reason FSDB classified the file error.
/// </param>
/// <param name="Persistence">
/// Whether FSDB classified the file error as temporary or persistent.
/// </param>
/// <param name="ExceptionType">
/// The full CLR type name of the exception observed while processing the file.
/// </param>
/// <param name="Message">
/// The exception message captured while processing the file.
/// </param>
/// <param name="HResult">
/// The exception HRESULT captured while processing the file.
/// </param>
public sealed record FileErrorInfo(
    FileErrorReason Reason,
    FileErrorPersistence Persistence,
    string ExceptionType,
    string Message,
    int HResult)
{
    // TODO: We probably don't need persistence
    public static FileErrorInfo Create(
        FileErrorReason reason,
        FileErrorPersistence persistence,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(reason,
            persistence,
            exception.GetType().FullName!,
            exception.Message,
            exception.HResult);
    }
}
