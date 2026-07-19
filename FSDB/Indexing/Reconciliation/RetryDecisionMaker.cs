using FSDB.FileStorage;
using FSDB.Retry;

namespace FSDB.Indexing.Reconciliation;

public class RetryDecisionMaker
{
    public RetryDecision MakeDecision(FileError? latestFileError, bool idLockMismatch)
    {
        // See docs/index-reconciliation-rulebook.md, Chapter 3: Retry Decision.
        if (idLockMismatch)
        {
            return RetryDecision.RetryWithMinBackoff;
        }

        return latestFileError?.Persistence == FileErrorPersistence.Transient
            ? RetryDecision.RetryWithBackoff
            : RetryDecision.Complete;
    }
}
