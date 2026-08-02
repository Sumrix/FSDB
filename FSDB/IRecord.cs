namespace FSDB;

public interface IRecord<out TKey>
{
    TKey Id { get; }
}
