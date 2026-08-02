namespace FolderDB;

public interface IVersionedRecord
{
    int SchemaVersion { get; }
}
