using System;
using System.Collections.Generic;
using System.Linq;
using FSDB.Collections;
using FSDB.Files;
using FSDB.Helpers;

namespace FSDB.Index.State;

/// <summary>
/// Stores the mutable in-memory state of a single record.
/// </summary>
/// <remarks>
/// A record must have at least one file.
/// </remarks>
public class RecordIndexState<TKey, TProjection> : IReadOnlyRecordIndexState<TKey, TProjection>
{
    private readonly CovariantReadOnlyDictionary<
        string,
        FileIndexState<TKey, TProjection>,
        IReadOnlyFileIndexState<TKey, TProjection>> _readOnlyFiles;
    private string? _currentFileName;

    public required TKey Id { get; init; }

    public Dictionary<string, FileIndexState<TKey, TProjection>> Files { get; }

    public string CurrentFileName => _currentFileName
        ?? throw new InvalidOperationException("Record index state does not have a current file.");

    IReadOnlyDictionary<string, IReadOnlyFileIndexState<TKey, TProjection>>
        IReadOnlyRecordIndexState<TKey, TProjection>.Files => _readOnlyFiles;

    public RecordIndexState()
    {
        Files = new(PathHelper.OSDependedPathComparer);
        _readOnlyFiles = new(Files);
    }

    public void RecalculateCurrent()
    {
        _currentFileName = Files
            .Select(kv => new FileSelectionKey(
                GetPriority(kv.Value.Status, kv.Value.ErrorInfo),
                kv.Value.Fingerprint.LastWriteUtc,
                kv.Key))
            .Max(FileComparer.Instance)
            ?.FileName
            ?? throw new InvalidOperationException("Record index state does not have files.");
    }

    private static int GetPriority(FileIndexStatus status, FileErrorInfo? errorInfo)
    {
        if (status == FileIndexStatus.Reserved)
            return 0;

        if (errorInfo is null)
            return 3;

        return errorInfo.Reason switch
        {
            FileErrorReason.Unavailable => 2,
            FileErrorReason.Invalid => 1,
            _ => 0
        };
    }

    private class FileComparer : IComparer<FileSelectionKey?>
    {
        public static FileComparer Instance { get; } = new();

        public int Compare(FileSelectionKey? x, FileSelectionKey? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var cmp = x.Priority.CompareTo(y.Priority);
            if (cmp != 0) return cmp;

            cmp = Nullable.Compare(x.LastWriteUtc, y.LastWriteUtc);
            if (cmp != 0) return cmp;

            return -PathHelper.OSDependedPathComparer.Compare(x.FileName, y.FileName);
        }
    }

    private sealed record FileSelectionKey(
        int Priority,
        DateTime? LastWriteUtc,
        string FileName);

    public FileIndexState<TKey, TProjection> GetCurrentFileState()
    {
        return Files[CurrentFileName];
    }
}
