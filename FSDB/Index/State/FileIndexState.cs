using FSDB.Files;

namespace FSDB.Index.State;

/// <summary>
/// Stores the mutable in-memory state of a single file.
/// </summary>
public class FileIndexState<TKey, TProjection> : IReadOnlyFileIndexState<TKey, TProjection>
{
    public required RecordIndexState<TKey, TProjection> Record { get; set; }

    public required FileIndexStatus Status { get; set; }

    public FileErrorInfo? ErrorInfo { get; set; }

    public TProjection? Projection { get; set; }

    public required FileFingerprint Fingerprint { get; set; }

    IReadOnlyRecordIndexState<TKey, TProjection> IReadOnlyFileIndexState<TKey, TProjection>.Record => Record;
}
