using System;
using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using FSDB.Index.Persistence;
using FSDB.Migration;
using FSDB.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSDB.Tests.TestSupport;

internal static class TestTableContext
{
    public static TableContext<TKey, TRecord, TProjection> Create<TKey, TRecord, TProjection>(
        Func<TRecord, TProjection> createProjection,
        Func<TRecord, IEnumerable<string>> generateFileNames,
        IEqualityComparer<TKey> keyEqualityComparer,
        IComparer<TKey> keyComparer,
        IRecordCodec<TKey, TRecord> codec)
        where TRecord : class, IRecord<TKey>
        where TKey : notnull
    {
        return new TableContext<TKey, TRecord, TProjection>
        {
            Name = typeof(TRecord).Name,
            KeyComparer = keyComparer,
            KeyEqualityComparer = keyEqualityComparer,
            FileNameGenerator = generateFileNames,
            CreateProjection = createProjection,
            RecordCodec = codec,
        };
    }

    public static ITableIndexPersistence<TKey, TProjection> CreateJsonIndexPersistence<TKey, TProjection>(
        JsonTypeInfo<TKey> keyTypeInfo,
        JsonTypeInfo<TProjection> projectionTypeInfo,
        IEqualityComparer<TKey> keyEqualityComparer)
        where TKey : notnull
    {
        return new JsonTableIndexPersistence<TKey, TProjection>(
            keyTypeInfo,
            projectionTypeInfo,
            keyEqualityComparer,
            NullLoggerFactory.Instance.CreateLogger<JsonTableIndexPersistence<TKey, TProjection>>());
    }
}
