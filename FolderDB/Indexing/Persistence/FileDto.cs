using FolderDB.FileStorage;
using FolderDB.Indexing.State;

namespace FolderDB.Indexing.Persistence;

internal sealed record FileDto(
    byte[]? Projection,
    FileFingerprint Fingerprint,
    FileIndexStatus Status = FileIndexStatus.Committed,
    FileErrorInfo? ErrorInfo = null,
    int? SchemaVersion = null);
