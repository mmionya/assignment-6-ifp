namespace Astra.Core;

// Map changes a successful value. Bind runs the next step, which can also fail.
public sealed class Result<T>
{
    private readonly T? value;
    public string? Error { get; }
    public bool IsSuccess => Error is null;
    public T Value => IsSuccess && value is T present
        ? present : throw new InvalidOperationException(Error ?? "No value.");

    private Result(T? value, string? error) => (this.value, Error) = (value, error);

    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value, null);
    }

    public static Result<T> Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new(default, error);
    }

    public Result<U> Map<U>(Func<T, U> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return Error is { } error
            ? Result<U>.Failure(error) : Result<U>.Success(transform(Value));
    }

    public Result<U> Bind<U>(Func<T, Result<U>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return Error is { } error
            ? Result<U>.Failure(error)
            : next(Value) ?? throw new InvalidOperationException("Bind must return a Result.");
    }

    // Handle the outcome once, without reading a failed result's Value.
    public U Match<U>(Func<T, U> onSuccess, Func<string, U> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return Error is { } error ? onFailure(error) : onSuccess(Value);
    }
}
