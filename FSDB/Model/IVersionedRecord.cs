namespace FSDB.Model;

public interface IVersionedRecord
{
    int SchemaVersion { get; }
}
