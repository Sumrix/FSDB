using System;
using FSDB.Files;
using FSDB.Index.State;

namespace FSDB.Tests;

public class RecordIndexStateTests
{
    [Fact]
    public void RecalculateCurrent_WhenCommittedAndNewerErrorInfoFilesExist_PrefersCommittedFile()
    {
        var record = new RecordIndexState<string, string> { Id = "id-1" };
        AddFile(
            record,
            "committed.json",
            FileIndexStatus.Committed,
            new DateTime(2025, 01, 01, 0, 0, 0, DateTimeKind.Utc));
        AddFile(
            record,
            "unavailable.json",
            FileErrorReason.Unavailable,
            new DateTime(2025, 01, 01, 0, 0, 2, DateTimeKind.Utc));
        AddFile(
            record,
            "invalid.json",
            FileErrorReason.Invalid,
            new DateTime(2025, 01, 01, 0, 0, 3, DateTimeKind.Utc));

        record.RecalculateCurrent();

        Assert.Equal("committed.json", record.CurrentFileName);
    }

    [Fact]
    public void RecalculateCurrent_WhenOnlyErrorInfoFilesExist_PrefersUnavailableOverInvalid()
    {
        var record = new RecordIndexState<string, string> { Id = "id-1" };
        AddFile(
            record,
            "invalid.json",
            FileErrorReason.Invalid,
            new DateTime(2025, 01, 01, 0, 0, 3, DateTimeKind.Utc));
        AddFile(
            record,
            "unavailable.json",
            FileErrorReason.Unavailable,
            new DateTime(2025, 01, 01, 0, 0, 0, DateTimeKind.Utc));

        record.RecalculateCurrent();

        Assert.Equal("unavailable.json", record.CurrentFileName);
    }

    [Fact]
    public void RecalculateCurrent_WhenStatusAndWriteTimeMatch_PrefersFirstFileName()
    {
        var record = new RecordIndexState<string, string> { Id = "id-1" };
        var timestamp = new DateTime(2025, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        AddFile(record, "b.json", FileIndexStatus.Committed, timestamp);
        AddFile(record, "a.json", FileIndexStatus.Committed, timestamp);

        record.RecalculateCurrent();

        Assert.Equal("a.json", record.CurrentFileName);
    }

    private static void AddFile(
        RecordIndexState<string, string> record,
        string fileName,
        FileIndexStatus status,
        DateTime lastWriteUtc)
    {
        record.Files[fileName] = new FileIndexState<string, string>
        {
            Record = record,
            Status = status,
            Projection = status == FileIndexStatus.Committed ? fileName : null,
            Fingerprint = new FileFingerprint(lastWriteUtc, 1, true)
        };
    }

    private static void AddFile(
        RecordIndexState<string, string> record,
        string fileName,
        FileErrorReason errorReason,
        DateTime lastWriteUtc)
    {
        record.Files[fileName] = new FileIndexState<string, string>
        {
            Record = record,
            Status = FileIndexStatus.Committed,
            ErrorInfo = new FileErrorInfo(
                errorReason,
                FileErrorPersistence.Persistent,
                typeof(InvalidOperationException).FullName!,
                "ErrorInfo",
                0),
            Projection = null,
            Fingerprint = new FileFingerprint(lastWriteUtc, 1, true)
        };
    }
}
