namespace FSDB.Indexing.Reconciliation;

internal enum FileUpdateIntent
{
    DoNothing,
    ReadFile,
    UpdateIfCurrentFile
}
