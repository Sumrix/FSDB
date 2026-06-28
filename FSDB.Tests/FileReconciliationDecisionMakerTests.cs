using System;
using System.IO;
using FluentAssertions;
using FSDB.Encoding;
using FSDB.FileStorage;
using FSDB.Indexing.Reconciliation;
using FSDB.Indexing.State;
using FSDB.Tests.TestSupport;

namespace FSDB.Tests;

public class FileReconciliationDecisionMakerTests
{
    private static readonly FileFingerprint _fileFingerprint =
        new(new DateTime(2026, 06, 20, 12, 0, 0, DateTimeKind.Utc), 100, Exists: true);
    private static readonly FileFingerprint _differentFingerprint =
        new(new DateTime(2026, 06, 20, 12, 0, 1, DateTimeKind.Utc), 101, Exists: true);
    private static readonly FileFingerprint _missingFingerprint = new(null, null, Exists: false);
    private static readonly FileError _sameError =
        new(FileErrorReason.Invalid, FileErrorPersistence.Persistent, new InvalidDataException("same error"));
    private static readonly FileError _differentError =
        new(FileErrorReason.Invalid, FileErrorPersistence.Persistent, new InvalidDataException("different error"));

    private readonly FileReconciliationDecisionMaker<string, TestRecord, string> _decisionMaker =
        new(StringComparer.OrdinalIgnoreCase);

    public static TheoryData<string, bool, bool, bool, bool, FileReconciliationDecision> PreReadCases
    {
        get
        {
            var result = new TheoryData<string, bool, bool, bool, bool, FileReconciliationDecision>();

            for (uint mask = 0; mask < 16; mask++)
            {
                BitMask m = mask;
                var testCase = new PreReadCase(m[0], m[1], m[2], m[3]);
                var expected = GetExpectedPreReadDecision(m[0], m[1], m[2], m[3]);
                result.Add(testCase.ToString(), m[0], m[1], m[2], m[3], expected);
            }

            return result;
        }
    }

    public static TheoryData<string, bool, bool, bool, bool, bool, bool, bool, FileReconciliationDecision> PostReadCases
    {
        get
        {
            var result = new TheoryData<string, bool, bool, bool, bool, bool, bool, bool, FileReconciliationDecision>();

            for (uint mask = 0; mask < 128; mask++)
            {
                BitMask m = mask;
                var testCase = new PostReadCase(m[0], m[1], m[2], m[3], m[4], m[5], m[6]);
                var expected = GetExpectedPostReadDecision(m[0], m[1], m[2], m[3], m[4], m[5], m[6]);
                result.Add(testCase.ToString(), m[0], m[1], m[2], m[3], m[4], m[5], m[6], expected);
            }

            return result;
        }
    }

    [Theory]
    [MemberData(nameof(PreReadCases))]
    public void MakePreReadDecision_CoversCompleteInputSpace(
        string _,
        bool filePresent,
        bool statePresent,
        bool stateError,
        bool sameFingerprint,
        FileReconciliationDecision expected)
    {
        var fileFingerprint = filePresent ? _fileFingerprint : _missingFingerprint;
        var indexedState = statePresent
            ? CreateIndexedState(
                id: "record-id",
                fingerprint: sameFingerprint ? fileFingerprint : _differentFingerprint,
                errorInfo: stateError ? _sameError.ToErrorInfo() : null)
            : null;

        var result = _decisionMaker.MakePreReadDecision(fileFingerprint, indexedState);

        result.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(PostReadCases))]
    public void MakePostReadDecision_CoversCompleteInputSpace(
        string _,
        bool filePresent,
        bool statePresent,
        bool fileError,
        bool stateError,
        bool sameId,
        bool sameFingerprint,
        bool sameError,
        FileReconciliationDecision expected)
    {
        const string indexedId = "record-id";
        var fileFingerprint = filePresent ? _fileFingerprint : _missingFingerprint;
        var error = fileError
            ? sameError ? _sameError : _differentError
            : null;
        var indexedState = statePresent
            ? CreateIndexedState(
                indexedId,
                sameFingerprint ? fileFingerprint : _differentFingerprint,
                stateError ? _sameError.ToErrorInfo() : null)
            : null;
        var readResult = CreateReadResult(
            fileFingerprint,
            filePresent,
            fileError,
            sameId ? indexedId.ToUpperInvariant() : "different-id",
            error);

        var result = _decisionMaker.MakePostReadDecision(readResult, indexedState);

        result.Should().Be(expected);
    }

    private static FileReadResult<RecordDecodeResult<TestRecord>> CreateReadResult(
        FileFingerprint fingerprint,
        bool filePresent,
        bool fileError,
        string id,
        FileError? error)
    {
        if (!filePresent)
        {
            return new(default, fingerprint);
        }

        if (fileError)
        {
            return new(default, fingerprint, error);
        }

        var record = new TestRecord(id, SchemaVersion: 1, Value: "value");
        return new(new RecordDecodeResult<TestRecord>(
            Upgraded: false,
            SourceSchemaVersion: null,
            TargetSchemaVersion: null,
            record),
            fingerprint);
    }

    private static IReadOnlyFileIndexState<string, string> CreateIndexedState(
        string id,
        FileFingerprint fingerprint,
        FileErrorInfo? errorInfo)
    {
        var record = new RecordIndexState<string, string> { Id = id };
        return new FileIndexState<string, string>
        {
            Record = record,
            Status = FileIndexStatus.Committed,
            ErrorInfo = errorInfo,
            Projection = errorInfo is null ? "projection" : null,
            Fingerprint = fingerprint
        };
    }

    // ReSharper disable InconsistentNaming
    private sealed record PreReadCase(bool PF, bool PS, bool ES, bool RF);

    private sealed record PostReadCase(bool PF, bool PS, bool EF, bool ES, bool RI, bool RF, bool RE);

    // Direct executable copy of the decision trees from docs/index-reconciliation-rulebook.md.
    // Keep these methods structurally aligned with the rulebook.
    private static FileReconciliationDecision GetExpectedPreReadDecision(bool pf, bool ps, bool es, bool rf)
    {
        return (pf, ps) switch
        {
            (false, false) => FileReconciliationDecision.Skip,
            (false, true) => FileReconciliationDecision.Delete,
            (true, false) => FileReconciliationDecision.ReadFile,
            (true, true) => (es, rf) switch
            {
                (false, true) => FileReconciliationDecision.Skip,
                _ => FileReconciliationDecision.ReadFile
            }
        };
    }

    private static FileReconciliationDecision GetExpectedPostReadDecision(
        bool pf,
        bool ps,
        bool ef,
        bool es,
        bool ri,
        bool rf,
        bool re)
    {
        return (pf, ps) switch
        {
            (false, false) => FileReconciliationDecision.Skip,
            (false, true) => FileReconciliationDecision.Delete,
            (true, false) => ef switch
            {
                false => FileReconciliationDecision.UpsertRecord,
                true => FileReconciliationDecision.Skip
            },
            (true, true) => (ef, es) switch
            {
                (false, false) => (ri, rf) switch
                {
                    (true, true) => FileReconciliationDecision.Skip,
                    (true, false) => FileReconciliationDecision.UpsertRecord,
                    (false, _) => FileReconciliationDecision.DeleteThenUpsertRecord,
                },
                (true, false) => FileReconciliationDecision.UpsertError,
                (false, true) => ri switch
                {
                    true => FileReconciliationDecision.UpsertRecord,
                    false => FileReconciliationDecision.DeleteThenUpsertRecord
                },
                (true, true) => (rf, re) switch
                {
                    (true, true) => FileReconciliationDecision.Skip,
                    _ => FileReconciliationDecision.UpsertError
                }
            }
        };
    }
}
