using FSDB.Files;

namespace FSDB.Tables;

/// <summary>
/// Describes the result of a file-aware read operation.
/// </summary>
/// <param name="Record">The record read from the selected file, or <see langword="null"/> when the operation did not return a record.</param>
/// <param name="Error">The file error observed by the operation, or <see langword="null"/> when no file error occurred.</param>
/// <param name="FileName">The name of the file from which the record was read, or <see langword="null"/> if no file was involved.</param>
public sealed record ReadResult<TRecord>(
    TRecord? Record,
    FileError? Error = null,
    string? FileName = null)
    : OperationResult(Error, FileName);