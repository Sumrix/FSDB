using System.Text.Json.Serialization;
using FSDB.Model;

namespace FSDB.Indexing.Persistence;

[JsonSerializable(typeof(NoProjection))]
[JsonSerializable(typeof(IndexDto))]
internal partial class IndexDtoJsonContext : JsonSerializerContext;
