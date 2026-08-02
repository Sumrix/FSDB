using System;
using FolderDB.Encoding;

namespace FolderDB.Building;

internal record RecordCodecStep<TKey, TRecord>
(
    Func<RecordCodecContext, IRecordCodec<TKey, TRecord>> RecordCodecFactory
)
    where TRecord : IRecord<TKey>;