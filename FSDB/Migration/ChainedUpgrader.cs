using System;
using FSDB.Exceptions;
using FSDB.Tables;

namespace FSDB.Migration;

public class ChainedUpgrader<TFrom, TNext, TCurrent>(
    Func<TFrom, TNext> upgradeFunction,
    IRecordUpgrader<TNext, TCurrent> nextUpgrader,
    int expectedVersion)
    : IRecordUpgrader<TFrom, TCurrent>
    where TNext : class, IVersionedRecord
{
    public TCurrent Upgrade(TFrom record)
    {
        var next = upgradeFunction(record);
        if (next.SchemaVersion != expectedVersion)
        {
            throw new RecordConversionException(
                $"Upgrade produced unexpected schema version: expected={expectedVersion}, actual={next.SchemaVersion}, type={typeof(TNext).Name}.");
        }

        return nextUpgrader.Upgrade(next);
    }
}
