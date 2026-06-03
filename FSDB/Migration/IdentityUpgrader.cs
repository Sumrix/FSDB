namespace FSDB.Migration;

public class IdentityUpgrader<TCurrent> : IRecordUpgrader<TCurrent, TCurrent>
{
    public TCurrent Upgrade(TCurrent record)
    {
        return record;
    }
}
