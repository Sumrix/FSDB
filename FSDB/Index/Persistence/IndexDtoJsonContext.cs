using System.Text.Json.Serialization;
using FSDB.Tables;

namespace FSDB.Index.Persistence;

[JsonSerializable(typeof(NoProjection))]
[JsonSerializable(typeof(IndexDto))]
internal partial class IndexDtoJsonContext : JsonSerializerContext;
