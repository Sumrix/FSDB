using System;
using System.Text.Json.Serialization.Metadata;

namespace FSDB.Encoding;

/// <summary>
/// Defines how a record type is decoded from disk and which schema version is considered current.
/// </summary>
/// <typeparam name="TRecord">The record type produced by this policy.</typeparam>
public class DecoderPolicy<TRecord>
{
    /// <summary>
    /// Initializes a new decoder policy.
    /// </summary>
    /// <param name="decoder">The decoder that reads records from JSON and applies schema handling.</param>
    /// <param name="jsonTypeInfo">The serializer metadata used to write records.</param>
    /// <param name="currentSchemaVersion">The current schema version for versioned records, or <see langword="null"/> for non-versioned records.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="decoder"/> or <paramref name="jsonTypeInfo"/> is <see langword="null"/>.</exception>
    public DecoderPolicy(
        ISchemaAwareRecordDecoder<TRecord> decoder,
        JsonTypeInfo<TRecord> jsonTypeInfo,
        int? currentSchemaVersion = null)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        Decoder = decoder;
        JsonTypeInfo = jsonTypeInfo;
        CurrentSchemaVersion = currentSchemaVersion;
    }

    /// <summary>
    /// Gets the decoder used to read records from JSON.
    /// </summary>
    public ISchemaAwareRecordDecoder<TRecord> Decoder { get; }

    /// <summary>
    /// Gets the serializer metadata used to write records.
    /// </summary>
    public JsonTypeInfo<TRecord> JsonTypeInfo { get; }

    /// <summary>
    /// Gets the current schema version, or <see langword="null"/> when versioning is not used.
    /// </summary>
    public int? CurrentSchemaVersion { get; }
}
