using System;
using System.Collections.Generic;
using System.IO;
using FSDB.FileStorage;
using FSDB.Indexing.State;
using FSDB.Runtime;

namespace FSDB.Tests;

public class IndexEntryViewTests
{
    [Fact]
    public void Index_WhenRecordHasOnlyReservedFile_ExcludesRecord()
    {
        var record = new RecordIndexState<string, string> { Id = "id-1" };
        record.Files["reserved.json"] = new FileIndexState<string, string>
        {
            Record = record,
            Status = FileIndexStatus.Reserved,
            Fingerprint = default
        };
        record.RecalculateCurrent();
        var view = new IndexEntryView<string, string>(new Dictionary<string, IReadOnlyRecordIndexState<string, string>>
        {
            ["id-1"] = record
        });

        Assert.Empty(view);
        Assert.False(view.ContainsKey("id-1"));
    }

    [Fact]
    public void Index_WhenRecordHasCurrentErrorInfoFile_IncludesRecord()
    {
        var record = new RecordIndexState<string, string> { Id = "id-1" };
        record.Files["unavailable.json"] = new FileIndexState<string, string>
        {
            Record = record,
            Status = FileIndexStatus.Committed,
            ErrorInfo = FileErrorInfo.Create(
                FileErrorReason.Unavailable,
                FileErrorPersistence.Persistent,
                new IOException("locked")),
            Fingerprint = new FileFingerprint(DateTime.UnixEpoch, 1, true)
        };
        record.RecalculateCurrent();
        var view = new IndexEntryView<string, string>(new Dictionary<string, IReadOnlyRecordIndexState<string, string>>
        {
            ["id-1"] = record
        });

        var entry = Assert.Single(view).Value;
        Assert.Equal("unavailable.json", entry.FileName);
        Assert.Equal(FileErrorReason.Unavailable, entry.ErrorInfo?.Reason);
    }
}
