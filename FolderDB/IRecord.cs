namespace FolderDB;

public interface IRecord<out TKey>
{
    TKey Id { get; }
}
