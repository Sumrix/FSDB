namespace FSDB.Indexing.Reconciliation;

public enum FileReconciliationDecision
{
    Skip,
    ReadFile,
    Delete,
    UpsertRecord,
    UpsertError,
    DeleteThenUpsertRecord
}
