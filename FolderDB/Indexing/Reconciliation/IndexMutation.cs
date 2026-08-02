namespace FolderDB.Indexing.Reconciliation;

internal enum IndexMutation : byte
{
    None,
    Delete,
    UpsertRecord,
    UpsertError
}
