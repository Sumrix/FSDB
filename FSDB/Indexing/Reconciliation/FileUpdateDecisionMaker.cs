using System;
using FSDB.Encoding;
using FSDB.FileStorage;
using FSDB.Indexing.State;
using FSDB.Infrastructure.Helpers;
using FSDB.Model;

namespace FSDB.Indexing.Reconciliation;

public class FileUpdateDecisionMaker<TKey, TRecord, TProjection>(int currentSchemaVersion)
    where TRecord : IRecord<TKey>
{
    public FileUpdateIntent MakePreReadIntent(
        string fileName,
        FileFingerprint fileFingerprint,
        IReadOnlyFileIndexState<TKey, TProjection>? indexedState)
    {
        if (!fileFingerprint.Exists)
        {
            return FileUpdateIntent.DoNothing;
        }

        if (indexedState is null ||
            indexedState.ErrorInfo is not null ||
            indexedState.Fingerprint != fileFingerprint)
        {
            return FileUpdateIntent.ReadFile;
        }

        var isCurrentFile = PathHelper.OSDependedPathComparer.Equals(
            fileName,
            indexedState.Record.CurrentFileName);
        return indexedState.SchemaVersion != currentSchemaVersion && isCurrentFile
            ? FileUpdateIntent.ReadFile
            : FileUpdateIntent.DoNothing;
    }

    public FileUpdateIntent MakePostReadIntent(
        FileReadResult<RecordDecodeResult<TRecord>> readResult)
    {
        return readResult.IsSuccess &&
               readResult.Value.SourceSchemaVersion != readResult.Value.TargetSchemaVersion
            ? FileUpdateIntent.UpdateIfCurrentFile
            : FileUpdateIntent.DoNothing;
    }

    public FileUpdateDecision MakeDecision(FileUpdateIntent intent, bool isCurrentFile)
    {
        return intent switch
        {
            FileUpdateIntent.DoNothing => FileUpdateDecision.DoNothing,
            FileUpdateIntent.UpdateIfCurrentFile when isCurrentFile => FileUpdateDecision.UpdateFile,
            FileUpdateIntent.UpdateIfCurrentFile => FileUpdateDecision.DoNothing,
            FileUpdateIntent.ReadFile => throw new ArgumentOutOfRangeException(
                nameof(intent),
                intent,
                "A read intent must be resolved before making a file update decision."),
            _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, null)
        };
    }
}
