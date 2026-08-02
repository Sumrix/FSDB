namespace FolderDB.Encoding;

public interface IRecordUpgrader<in TFrom, out TNext>
{
    TNext Upgrade(TFrom record);
}
