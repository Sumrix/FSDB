namespace FolderDB.Indexing.Reconciliation;

internal readonly record struct FileReconciliationDecision
{
    private FileReconciliationDecision(
        IndexMutation indexedIdPart,
        IndexMutation diskIdPart,
        bool requiresRead = false)
    {
        IndexedIdPart = indexedIdPart;
        DiskIdPart = diskIdPart;
        RequiresRead = requiresRead;
    }

    // See docs/index-reconciliation-rulebook.md, Chapter 1: Analyze Data.
    public static readonly FileReconciliationDecision Skip =
        new(IndexMutation.None, IndexMutation.None);

    public static readonly FileReconciliationDecision ReadFile =
        new(IndexMutation.None, IndexMutation.None, requiresRead: true);

    public static readonly FileReconciliationDecision Delete =
        new(IndexMutation.Delete, IndexMutation.None);

    public static readonly FileReconciliationDecision UpsertRecord =
        new(IndexMutation.None, IndexMutation.UpsertRecord);

    public static readonly FileReconciliationDecision UpsertError =
        new(IndexMutation.UpsertError, IndexMutation.None);

    public static readonly FileReconciliationDecision DeleteThenUpsertRecord =
        new(IndexMutation.Delete, IndexMutation.UpsertRecord);

    public IndexMutation IndexedIdPart { get; }

    public IndexMutation DiskIdPart { get; }

    public bool RequiresRead { get; }

    public override string ToString()
    {
        if (RequiresRead)
        {
            return nameof(ReadFile);
        }

        return (IndexedIdPart, DiskIdPart) switch
        {
            (IndexMutation.None, IndexMutation.None) => nameof(Skip),
            (IndexMutation.None, var diskIdPart) => diskIdPart.ToString(),
            (var indexedIdPart, IndexMutation.None) => indexedIdPart.ToString(),
            var (indexedIdPart, diskIdPart) => $"{indexedIdPart}Then{diskIdPart}"
        };
    }
}
