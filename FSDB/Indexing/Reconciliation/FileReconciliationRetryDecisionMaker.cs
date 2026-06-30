using FSDB.FileStorage;
using FSDB.Retry;

namespace FSDB.Indexing.Reconciliation;

public class FileReconciliationRetryDecisionMaker
{
    public RetryDecision MakeDecision(FileError? latestReadError, bool idLockMismatch)
    {
        // See docs/index-reconciliation-rulebook.md, Chapter 3: Retry Decision.
        if (idLockMismatch)
        {
            return RetryDecision.RetryWithMinBackoff;
        }

        return latestReadError?.Persistence == FileErrorPersistence.Transient
            ? RetryDecision.RetryWithBackoff
            : RetryDecision.Complete;
    }
}
