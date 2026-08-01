namespace FSDB.Indexing.Reconciliation;

internal enum IndexMutation : byte
{
    None,
    Delete,
    UpsertRecord,
    UpsertError
}
