using System.Text.Json.Serialization;
using FSDB.Model;

namespace FSDB.Tests.TestSupport;

[JsonSerializable(typeof(TestRecord))]
[JsonSerializable(typeof(PlainTestRecord))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(NoProjection))]
internal partial class TestsJsonContext : JsonSerializerContext;
