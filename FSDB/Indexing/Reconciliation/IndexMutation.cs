namespace FSDB.Indexing.Reconciliation;

public enum IndexMutation : byte
{
    None,
    Delete,
    UpsertRecord,
    UpsertError
}
