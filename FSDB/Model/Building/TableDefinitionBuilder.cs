using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace FSDB.Model.Building;

public static class TableDefinitionBuilder
{
    public static RecordCodecBuilder<TKey, TRecord> Create<TKey, TRecord>()
        where TRecord : class, IRecord<TKey>
        where TKey : notnull
    {
        return new RecordCodecBuilder<TKey, TRecord>(
            new TableOptions<TKey, TRecord>());
    }

    [RequiresUnreferencedCode("Uses reflection-based resolver via populateMissingResolver.")]
    [RequiresDynamicCode("May require runtime code generation for reflection-based serialization.")]
    public static TableDefinition<TKey, TRecord, NoProjection> CreateDefault<TKey, TRecord>(
        JsonSerializerOptions? jsonOptions = null,
        TableOptions<TKey, TRecord>? tableOptions = null)
        where TRecord : class, IRecord<TKey>
        where TKey : notnull
    {
        return CreateDefault<TKey, TRecord, NoProjection>(
            static _ => default,
            jsonOptions,
            tableOptions);
    }

    [RequiresUnreferencedCode("Uses reflection-based resolver via populateMissingResolver.")]
    [RequiresDynamicCode("May require runtime code generation for reflection-based serialization.")]
    public static TableDefinition<TKey, TRecord, TProjection> CreateDefault<TKey, TRecord, TProjection>(
        Func<TRecord, TProjection> createProjection,
        JsonSerializerOptions? jsonOptions = null,
        TableOptions<TKey, TRecord>? tableOptions = null)
        where TRecord : class, IRecord<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(createProjection);

        tableOptions ??= new TableOptions<TKey, TRecord>();
        return new RecordCodecBuilder<TKey, TRecord>(tableOptions)
            .UseJsonRecordCodec(jsonOptions)
            .WithProjection(createProjection)
            .UseJsonIndexPersistence(jsonOptions)
            .Build();
    }
}
