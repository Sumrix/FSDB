using FSDB.FileStorage;
using FSDB.Indexing.State;

namespace FSDB.Indexing.Persistence;

internal sealed record FileDto(
    byte[]? Projection,
    FileFingerprint Fingerprint,
    FileIndexStatus Status = FileIndexStatus.Committed,
    FileErrorInfo? ErrorInfo = null,
    int? SchemaVersion = null);
