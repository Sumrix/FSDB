using System.Collections.Generic;

namespace FSDB.Index.Persistence;

internal sealed record RecordDto(byte[] Key, Dictionary<string, FileDto> Files);
