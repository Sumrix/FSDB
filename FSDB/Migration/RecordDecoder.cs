using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FSDB.Exceptions;

namespace FSDB.Migration;

public class RecordDecoder<TFrom, TCurrent>(
    IRecordUpgrader<TFrom, TCurrent> upgrader,
    JsonTypeInfo<TFrom> typeInfo)
    : IRecordDecoder<TCurrent>
{
    public TCurrent Decode(JsonDocument document)
    {
        try
        {
            var record = document.Deserialize(typeInfo)
                ?? throw new RecordConversionException($"Failed to deserialize JSON to {typeof(TFrom).Name}");

            return upgrader.Upgrade(record);
        }
        catch (RecordConversionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RecordConversionException(
                $"Failed to convert record from {typeof(TFrom).Name} to {typeof(TCurrent).Name}.",
                inner: ex);
        }
    }
}
