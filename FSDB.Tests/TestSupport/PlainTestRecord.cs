using FSDB.Tables;

namespace FSDB.Tests.TestSupport;

public sealed record PlainTestRecord(string Id, string Value) : IRecord<string>;
