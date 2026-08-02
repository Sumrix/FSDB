namespace FSDB;

public interface IVersionedRecord
{
    int SchemaVersion { get; }
}
