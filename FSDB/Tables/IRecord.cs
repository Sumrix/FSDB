namespace FSDB.Tables;

public interface IRecord<out TKey>
{
    TKey Id { get; }
}
