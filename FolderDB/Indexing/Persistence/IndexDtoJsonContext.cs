using System.Text.Json.Serialization;

namespace FolderDB.Indexing.Persistence;

[JsonSerializable(typeof(NoProjection))]
[JsonSerializable(typeof(IndexDto))]
internal partial class IndexDtoJsonContext : JsonSerializerContext;
