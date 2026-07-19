namespace FSDB.Indexing.Reconciliation;

public enum FileUpdateIntent
{
    DoNothing,
    ReadFile,
    UpdateIfCurrentFile
}
