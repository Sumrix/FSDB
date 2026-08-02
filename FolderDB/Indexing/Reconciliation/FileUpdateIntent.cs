namespace FolderDB.Indexing.Reconciliation;

internal enum FileUpdateIntent
{
    DoNothing,
    ReadFile,
    UpdateIfCurrentFile
}
