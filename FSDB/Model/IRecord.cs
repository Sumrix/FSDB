namespace FSDB.Model;

public interface IRecord<out TKey>
{
    TKey Id { get; }
}
