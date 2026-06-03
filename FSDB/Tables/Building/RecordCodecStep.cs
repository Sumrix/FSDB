using System;
using FSDB.Migration;

namespace FSDB.Tables.Building;

internal record RecordCodecStep<TKey, TRecord>
(
    Func<RecordCodecContext, IRecordCodec<TKey, TRecord>> RecordCodecFactory
)
    where TRecord : IRecord<TKey>;