using System;
using System.Diagnostics.CodeAnalysis;

namespace FSDB.Primitives;

public readonly struct Result<T>
{
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Exception))]
    public bool IsSuccess { get; }

    [MemberNotNullWhen(false, nameof(Value))]
    [MemberNotNullWhen(true, nameof(Exception))]
    public bool IsFailure => !IsSuccess;

    public T? Value { get; }

    public Exception? Exception { get; }

    public Result(T value)
    {
        IsSuccess = true;
        Value = value;
        Exception = null;
    }

    public Result(Exception exception)
    {
        IsSuccess = false;
        Value = default;
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Exception exception) => new(exception);

    public Result<U> Map<U>(Func<T, U> selector)
    {
        return IsSuccess ? selector(Value!) : Exception!;
    }

    public Result<U> FlatMap<U>(Func<T, Result<U>> selector)
    {
        return IsSuccess ? selector(Value!) : Exception!;
    }

    public Result<T> MapFailure(Func<Exception, Exception> selector)
    {
        return IsFailure ? selector(Exception!) : this;
    }

    public Result<T> Recover(Func<Exception, T> selector)
    {
        return IsSuccess ? Value! : selector(Exception!);
    }

    public T ValueOrDefault(T defaultValue = default!)
    {
        return IsSuccess ? Value! : defaultValue;
    }

    public void Deconstruct(out bool isSuccess, out T? value, out Exception? exception)
    {
        isSuccess = IsSuccess;
        value = Value;
        exception = Exception;
    }
}

public static class Result
{
    public static Result<T> Ok<T>(T value) => new(value);

    public static Result<T> Fail<T>(Exception exception) => new(exception);
}