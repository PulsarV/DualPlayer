using System.Diagnostics.CodeAnalysis;

namespace DualPlayer.Core;

/// <summary>An expected runtime failure. Exceptions mean bugs; expected failures travel as <see cref="Error"/>.</summary>
public sealed record Error(string Code, string Message);

/// <summary>Outcome of an operation that returns no value.</summary>
public readonly record struct Result
{
    private Result(Error? error) => Error = error;

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    public static Result Failure(string code, string message) => Failure(new Error(code, message));
}

/// <summary>Outcome of an operation that returns a <typeparamref name="T"/> on success.</summary>
public readonly record struct Result<T>
{
    private Result(T? value, Error? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public Error? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result<T> Success(T value) => new(value ?? throw new ArgumentNullException(nameof(value)), null);

    public static Result<T> Failure(Error error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));

    public static Result<T> Failure(string code, string message) => Failure(new Error(code, message));
}
