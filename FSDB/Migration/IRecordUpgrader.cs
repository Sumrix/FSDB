namespace FSDB.Migration;

public interface IRecordUpgrader<in TFrom, out TNext>
{
    TNext Upgrade(TFrom record);
}
