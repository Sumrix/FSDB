using FSDB.Files;
using FSDB.Index.State;

namespace FSDB.Index.Persistence;

internal sealed record FileDto(
    byte[]? Projection,
    FileFingerprint Fingerprint,
    FileIndexStatus Status = FileIndexStatus.Committed,
    FileErrorInfo? ErrorInfo = null);
