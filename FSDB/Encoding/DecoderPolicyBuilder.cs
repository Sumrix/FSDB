using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FSDB.Model;

namespace FSDB.Encoding;

/// <summary>
/// Entry point for creating <see cref="DecoderPolicy{TRecord}"/> instances.
/// </summary>
public class DecoderPolicyBuilder
{
    /// <summary>
    /// Creates a decoder policy for a non-versioned record type using serializer metadata resolved from the provided options.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="options">The serializer options used to resolve <see cref="JsonTypeInfo{T}"/>. If <see langword="null"/>, <see cref="JsonSerializerOptions.Default"/> is used.</param>
    /// <returns>A decoder policy that reads and writes records without schema migration.</returns>
    /// <exception cref="InvalidOperationException">Thrown when serializer metadata for <typeparamref name="T"/> cannot be resolved from <paramref name="options"/>.</exception>
    [RequiresUnreferencedCode("Uses reflection-based resolver via populateMissingResolver.")]
    [RequiresDynamicCode("May require runtime code generation for reflection-based serialization.")]
    public DecoderPolicy<T> WithoutVersioning<T>(JsonSerializerOptions? options = null)
    {
        options ??= JsonSerializerOptions.Default;
        options.MakeReadOnly(populateMissingResolver: true);
        var jsonTypeInfo = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

        return new DecoderPolicy<T>(new DirectRecordDecoder<T>(jsonTypeInfo), jsonTypeInfo);
    }

    /// <summary>
    /// Creates a decoder policy for a non-versioned record type using explicit serializer metadata.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="jsonTypeInfo">The serializer metadata used for both reading and writing records.</param>
    /// <returns>A decoder policy that reads and writes records without schema migration.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jsonTypeInfo"/> is <see langword="null"/>.</exception>
    public DecoderPolicy<T> WithoutVersioning<T>(JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return new DecoderPolicy<T>(new DirectRecordDecoder<T>(jsonTypeInfo), jsonTypeInfo);
    }

    /// <summary>
    /// Starts building a decoder policy for a versioned record type using serializer metadata resolved from the provided options.
    /// </summary>
    /// <typeparam name="T">The first record type in the migration chain.</typeparam>
    /// <param name="version">The schema version represented by <typeparamref name="T"/>.</param>
    /// <param name="options">The serializer options used to resolve <see cref="JsonTypeInfo{T}"/>. If <see langword="null"/>, <see cref="JsonSerializerOptions.Default"/> is used.</param>
    /// <returns>A fluent builder that can add migration steps and build the final decoder policy.</returns>
    /// <exception cref="InvalidOperationException">Thrown when serializer metadata for <typeparamref name="T"/> cannot be resolved from <paramref name="options"/>.</exception>
    [RequiresUnreferencedCode("Uses reflection-based resolver via populateMissingResolver.")]
    [RequiresDynamicCode("May require runtime code generation for reflection-based serialization.")]
    public IDecoderPolicyBuilder<T> StartWith<T>(
        int version,
        JsonSerializerOptions? options = null)
        where T : class, IVersionedRecord
    {
        options ??= JsonSerializerOptions.Default;
        options.MakeReadOnly(populateMissingResolver: true);
        var jsonTypeInfo = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

        return new DecoderPolicyBuilderStep<T, T>(version, null, null, jsonTypeInfo);
    }

    /// <summary>
    /// Starts building a decoder policy for a versioned record type using explicit serializer metadata.
    /// </summary>
    /// <typeparam name="T">The first record type in the migration chain.</typeparam>
    /// <param name="version">The schema version represented by <typeparamref name="T"/>.</param>
    /// <param name="jsonTypeInfo">The serializer metadata used to read version <paramref name="version"/> records.</param>
    /// <returns>A fluent builder that can add migration steps and build the final decoder policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jsonTypeInfo"/> is <see langword="null"/>.</exception>
    public IDecoderPolicyBuilder<T> StartWith<T>(
        int version,
        JsonTypeInfo<T> jsonTypeInfo)
        where T : class, IVersionedRecord
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return new DecoderPolicyBuilderStep<T, T>(version, null, null, jsonTypeInfo);
    }
}
