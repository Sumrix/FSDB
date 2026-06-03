using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace FSDB.Primitives;

public readonly struct Option<T>
{
    [MemberNotNullWhen(true, nameof(Value))]
    public bool HasValue { get; }

    [MemberNotNullWhen(false, nameof(Value))]
    public bool IsNone => !HasValue;

    public T? Value { get; }

    private Option(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        HasValue = true;
        Value = value;
    }

    public static Option<T> Some(T value) => new(value);

    public static Option<T> None => default;

    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = Value!;
        return HasValue;
    }

    public T ValueOrDefault(T defaultValue = default!)
    {
        return HasValue ? Value! : defaultValue;
    }

    public Option<U> Map<U>(Func<T, U> selector)
    {
        return HasValue ? Option<U>.Some(selector(Value!)) : Option<U>.None;
    }

    public Option<U> FlatMap<U>(Func<T, Option<U>> selector)
    {
        return HasValue ? selector(Value!) : Option<U>.None;
    }

    public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none)
    {
        return HasValue ? some(Value!) : none();
    }

    public void Deconstruct(out bool hasValue, out T? value)
    {
        hasValue = HasValue;
        value = Value;
    }

    public static implicit operator Option<T>(T value) => Some(value);

    public bool Equals(Option<T> other, IEqualityComparer<T>? comparer = null)
    {
        if (HasValue != other.HasValue)
            return false;
        if (!HasValue) // Both are None
            return true;
        return (comparer ?? EqualityComparer<T>.Default).Equals(Value!, other.Value!);
    }
}

public static class Option
{
    public static Option<T> Some<T>(T value) => Option<T>.Some(value);

    public static Option<T> None<T>() => Option<T>.None;
}
