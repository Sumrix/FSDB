using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FSDB.Tables;

namespace FSDB.Migration;

internal sealed class DecoderPolicyBuilderStep<TFrom, TCurrent>
    : IRecordDecoderRegistryContributor<TCurrent>, IDecoderPolicyBuilder<TCurrent>
    where TFrom : class, IVersionedRecord
    where TCurrent : class, IVersionedRecord
{
    private readonly Func<TFrom, TCurrent>? _upgradeFunction;
    private readonly IRecordDecoderRegistryContributor<TFrom>? _previousNode;
    private readonly JsonTypeInfo<TCurrent> _nextTypeInfo;
    private readonly int _nextVersion;

    internal DecoderPolicyBuilderStep(
        int currentVersion,
        Func<TFrom, TCurrent>? upgradeFunction,
        IRecordDecoderRegistryContributor<TFrom>? previousNode,
        JsonTypeInfo<TCurrent> currentTypeInfo)
    {
        _nextVersion = currentVersion;
        _nextTypeInfo = currentTypeInfo;
        _upgradeFunction = upgradeFunction;
        _previousNode = previousNode;
    }

    [RequiresUnreferencedCode("Uses reflection-based resolver via populateMissingResolver.")]
    [RequiresDynamicCode("May require runtime code generation for reflection-based serialization.")]
    public IDecoderPolicyBuilder<TTo> UpgradeTo<TTo>(
        int toVersion,
        Func<TCurrent, TTo> upgrade,
        JsonSerializerOptions? options = null)
        where TTo : class, IVersionedRecord
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(toVersion, _nextVersion);
        ArgumentNullException.ThrowIfNull(upgrade);

        options ??= JsonSerializerOptions.Default;
        options.MakeReadOnly(populateMissingResolver: true);
        var toTypeInfo = (JsonTypeInfo<TTo>)options.GetTypeInfo(typeof(TTo));

        return new DecoderPolicyBuilderStep<TCurrent, TTo>(toVersion, upgrade, this, toTypeInfo);
    }

    public IDecoderPolicyBuilder<TTo> UpgradeTo<TTo>(
        int toVersion,
        Func<TCurrent, TTo> upgrade,
        JsonTypeInfo<TTo> toTypeInfo)
        where TTo : class, IVersionedRecord
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(toVersion, _nextVersion);
        ArgumentNullException.ThrowIfNull(upgrade);
        ArgumentNullException.ThrowIfNull(toTypeInfo);

        return new DecoderPolicyBuilderStep<TCurrent, TTo>(toVersion, upgrade, this, toTypeInfo);
    }

    public DecoderPolicy<TCurrent> Build()
    {
        var terminalUpgrader = new IdentityUpgrader<TCurrent>();
        var decoders = new Dictionary<int, IRecordDecoder<TCurrent>>();

        ((IRecordDecoderRegistryContributor<TCurrent>)this).ContributeTo(decoders, terminalUpgrader);

        return new DecoderPolicy<TCurrent>(
            new RegistryRecordDecoder<TCurrent>(decoders),
            _nextTypeInfo,
            _nextVersion);
    }

    void IRecordDecoderRegistryContributor<TCurrent>.ContributeTo<TTarget>(
        Dictionary<int, IRecordDecoder<TTarget>> decoderRegistry,
        IRecordUpgrader<TCurrent, TTarget> nextUpgrader)
    {
        var decoder = new RecordDecoder<TCurrent, TTarget>(nextUpgrader, _nextTypeInfo);
        if (!decoderRegistry.TryAdd(_nextVersion, decoder))
        {
            throw new InvalidOperationException("A decoder for version " + _nextVersion + " has already been registered.");
        }

        if (_upgradeFunction == null || _previousNode == null)
        {
            return;
        }

        var chainedUpgrader = new ChainedUpgrader<TFrom, TCurrent, TTarget>(_upgradeFunction, nextUpgrader, _nextVersion);
        _previousNode.ContributeTo(decoderRegistry, chainedUpgrader);
    }
}
