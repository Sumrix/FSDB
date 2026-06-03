namespace FSDB.Migration;

/// <summary>
/// Represents the result of decoding a record from storage.
/// </summary>
/// <typeparam name="TRecord">The decoded record type.</typeparam>
public readonly record struct RecordDecodeResult<TRecord>(
    bool Upgraded,
    int? SourceSchemaVersion,
    int? TargetSchemaVersion,
    TRecord Record);
