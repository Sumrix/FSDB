using System;

namespace FSDB.Model.Building;

internal record ProjectionStep<TKey, TRecord, TProjection>
(
    RecordCodecStep<TKey, TRecord> Previous,
    Func<TRecord, TProjection> CreateProjection
)
    where TRecord : IRecord<TKey>;