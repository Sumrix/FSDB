using System;

namespace FSDB.Tables.Building;

internal record ProjectionStep<TKey, TRecord, TProjection>
(
    RecordCodecStep<TKey, TRecord> Previous,
    Func<TRecord, TProjection> CreateProjection
)
    where TRecord : IRecord<TKey>;