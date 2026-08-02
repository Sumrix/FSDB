namespace FolderDB.Tests.TestSupport;

public sealed record TestRecord(string Id, int SchemaVersion, string Value) : IRecord<string>, IVersionedRecord;
