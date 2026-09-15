namespace BuildingBlocks.Results;

/// <summary>
/// The outcome of a use case that produces a <typeparamref name="T"/> on success. Every
/// interactor's <c>Handle</c> method returns <c>Task&lt;Result&lt;TResponse&gt;&gt;</c>
/// (CONVENTIONS.md "Use cases"/"Errors") — this is that return type. <see cref="Result"/> is the
/// non-generic counterpart for use cases with no value to carry.
/// </summary>
public readonly struct Result<T> : IEquatable<Result<T>>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(bool isSuccess, T? value, Error? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The success value. Throws if accessed on a failed result — callers must check
    /// <see cref="IsSuccess"/> (or use <see cref="Match{TOut}"/>) first; reaching for the value
    /// of a failure is a programming error, not an expected outcome.
    /// </summary>
    public T Value =>
        IsSuccess ? _value! : throw new InvalidOperationException("A failed result has no value.");

    /// <summary>
    /// The failure. Throws if accessed on a successful result. Also throws (with a different
    /// message) on a <c>default(Result&lt;T&gt;)</c> — a struct default reports
    /// <see cref="IsFailure"/> but was never constructed via <see cref="Failure"/>, so there is
    /// no error to hand back either.
    /// </summary>
    public Error Error
    {
        get
        {
            if (IsSuccess)
            {
                throw new InvalidOperationException("A successful result has no error.");
            }

            return _error ?? throw new InvalidOperationException(
                "This Result<T> reports failure but carries no Error — it is likely a " +
                "default(Result<T>) rather than one constructed via Result<T>.Failure().");
        }
    }

    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(true, value, null);
    }

    public static Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(false, default, error);
    }

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);

    /// <summary>Reduces the result to a single value without branching at the call site.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(_value!) : onFailure(Error);
    }

    public bool Equals(Result<T> other) =>
        IsSuccess == other.IsSuccess &&
        EqualityComparer<T?>.Default.Equals(_value, other._value) &&
        Equals(_error, other._error);

    public override bool Equals(object? obj) => obj is Result<T> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(IsSuccess, _value, _error);

    public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);

    public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);
}
