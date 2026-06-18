using System.Threading;
using System.Threading.Tasks;
using FSDB.Indexing.State;

namespace FSDB.Indexing.Persistence;

/// <summary>
/// Defines persistence for saving and loading table index state.
/// </summary>
public interface ITableIndexPersistence<TKey, TProjection>
    where TKey : notnull
{
    Task<TableIndexState<TKey, TProjection>?> LoadIfExistsAsync(
        string path,
        CancellationToken ct = default);

    byte[] SerializeToBytes(TableIndexState<TKey, TProjection> state);
}
