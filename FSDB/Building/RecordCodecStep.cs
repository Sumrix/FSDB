using System;
using FSDB.Encoding;

namespace FSDB.Building;

internal record RecordCodecStep<TKey, TRecord>
(
    Func<RecordCodecContext, IRecordCodec<TKey, TRecord>> RecordCodecFactory
)
    where TRecord : IRecord<TKey>;