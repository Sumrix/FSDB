namespace FolderDB.Encoding;

/// <summary>
/// Represents a point in the chain where a <see cref="RecordSchema{TRecord}"/> can be built.
/// </summary>
/// <typeparam name="TRecord">The record type produced by the record schema.</typeparam>
public interface IRecordSchemaFinalBuilder<TRecord>
{
    /// <summary>
    /// Builds the record schema configured by the current builder chain.
    /// </summary>
    /// <returns>The built record schema.</returns>
    RecordSchema<TRecord> Build();
}
