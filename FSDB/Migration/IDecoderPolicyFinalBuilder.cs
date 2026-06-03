namespace FSDB.Migration;

/// <summary>
/// Represents the final step of building a <see cref="DecoderPolicy{TRecord}"/>.
/// </summary>
/// <typeparam name="TRecord">The record type produced by the decoder policy.</typeparam>
public interface IDecoderPolicyFinalBuilder<TRecord>
{
    /// <summary>
    /// Builds the decoder policy configured by the current builder chain.
    /// </summary>
    /// <returns>The built decoder policy.</returns>
    /// <remarks>UwU</remarks>
    DecoderPolicy<TRecord> Build();
}
