using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FSDB.Migration;

namespace FSDB.Tables.Building;

public sealed class RecordCodecBuilder<TKey, TRecord>
    : TableOptionsBuilder<TKey, TRecord, RecordCodecBuilder<TKey, TRecord>>
    where TRecord : class, IRecord<TKey>
    where TKey : notnull
{
    protected override RecordCodecBuilder<TKey, TRecord> This => this;

    internal RecordCodecBuilder(TableOptions<TKey, TRecord> options)
        : base(options)
    {
    }

    public ProjectionBuilder<TKey, TRecord> UseRecordCodec(IRecordCodec<TKey, TRecord> recordCodec)
    {
        ArgumentNullException.ThrowIfNull(recordCodec);
        return UseRecordCodec(_ => recordCodec);
    }

    public ProjectionBuilder<TKey, TRecord> UseRecordCodec(
        Func<RecordCodecContext, IRecordCodec<TKey, TRecord>> recordCodecFactory)
    {
        ArgumentNullException.ThrowIfNull(recordCodecFactory);
        return new ProjectionBuilder<TKey, TRecord>(new(recordCodecFactory), Options);
    }

    public ProjectionBuilder<TKey, TRecord> UseJsonRecordCodec(DecoderPolicy<TRecord> decoderPolicy)
    {
        ArgumentNullException.ThrowIfNull(decoderPolicy);
        return UseRecordCodec(_ => new RecordCodec<TKey, TRecord>(decoderPolicy));
    }

    [RequiresUnreferencedCode("Uses reflection-based resolver via populateMissingResolver.")]
    [RequiresDynamicCode("May require runtime code generation for reflection-based serialization.")]
    public ProjectionBuilder<TKey, TRecord> UseJsonRecordCodec(JsonSerializerOptions? options = null)
    {
        return UseJsonRecordCodec(new DecoderPolicyBuilder().WithoutVersioning<TRecord>(options));
    }

    public ProjectionBuilder<TKey, TRecord> UseJsonRecordCodec(JsonTypeInfo<TRecord> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return UseJsonRecordCodec(new DecoderPolicyBuilder().WithoutVersioning(jsonTypeInfo));
    }

    public ProjectionBuilder<TKey, TRecord> UseJsonRecordCodec(
        Func<DecoderPolicyBuilder, IDecoderPolicyFinalBuilder<TRecord>> decoderPolicyFactory)
    {
        ArgumentNullException.ThrowIfNull(decoderPolicyFactory);

        var policyBuilder = decoderPolicyFactory(new DecoderPolicyBuilder());
        ArgumentNullException.ThrowIfNull(policyBuilder);

        return UseJsonRecordCodec(policyBuilder.Build());
    }
}
