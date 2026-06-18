using System;

namespace FSDB.FileStorage;

// TODO: Check if there are any benefits of being a struct vs class here.
/// <summary>
/// Represents the meaningful observed state of a file.
/// </summary>
public readonly record struct FileFingerprint(DateTime? LastWriteUtc, long? Length, bool Exists)
{
    public override string ToString()
        => Exists
            ? $"exists=true length={Length} lastWriteUtc={LastWriteUtc:O}"
            : "exists=false";
}
