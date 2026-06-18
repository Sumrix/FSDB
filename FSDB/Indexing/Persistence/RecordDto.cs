using System.Collections.Generic;

namespace FSDB.Indexing.Persistence;

internal sealed record RecordDto(byte[] Key, Dictionary<string, FileDto> Files);
