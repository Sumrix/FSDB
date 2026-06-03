using System.Collections.Generic;

namespace FSDB.Migration;

/// <summary>
/// Contributes record decoders to a registry by chaining migration steps.
/// </summary>
/// <typeparam name="TNext">The source type expected by the next upgrader in the chain.</typeparam>
internal interface IRecordDecoderRegistryContributor<out TNext>
{
    /// <summary>
    /// Recursively contributes record decoders to the provided registry.
    /// </summary>
    /// <typeparam name="TCurrent">The current type in the chain being built.</typeparam>
    /// <param name="decoderRegistry">The dictionary of decoders keyed by version number.</param>
    /// <param name="nextUpgrader">The next upgrade step to connect to.</param>
    public void ContributeTo<TCurrent>(
        Dictionary<int, IRecordDecoder<TCurrent>> decoderRegistry,
        IRecordUpgrader<TNext, TCurrent> nextUpgrader);
}
