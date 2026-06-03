namespace FSDB.Tables;

public interface IVersionedRecord
{
    int SchemaVersion { get; }
}
