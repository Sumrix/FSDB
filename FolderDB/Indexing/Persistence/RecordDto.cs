using System.Collections.Generic;

namespace FolderDB.Indexing.Persistence;

internal sealed record RecordDto(byte[] Key, Dictionary<string, FileDto> Files);
