using FSDB.Files;

namespace FSDB.Index.State;

/// <summary>
/// Exposes read-only information about an indexed file.
/// </summary>
public interface IReadOnlyFileIndexState<TKey, TProjection>
{
    IReadOnlyRecordIndexState<TKey, TProjection> Record { get; }

    FileIndexStatus Status { get; }

    FileErrorInfo? ErrorInfo { get; }

    TProjection? Projection { get; }

    FileFingerprint Fingerprint { get; }
}
