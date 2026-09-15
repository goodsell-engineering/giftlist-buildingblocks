namespace BuildingBlocks.Results;

/// <summary>
/// The outcome of a use case that produces no value on success — e.g. "delete gift list".
/// See <see cref="Result{T}"/> for the value-carrying counterpart.
/// </summary>
/// <remarks>
/// Exceptions are reserved for bugs and infrastructure failures (CONVENTIONS.md "Errors"); every use
/// case that can fail in an expected way returns one of these instead.
/// </remarks>
public readonly struct Result : IEquatable<Result>
{
    private readonly Error? _error;

    private Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The failure. Throws if accessed on a successful result — that would be a programming
    /// error, not an expected outcome, so it is an exception rather than a null. Also throws
    /// (with a different message) on a <c>default(Result)</c> — a struct default reports
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
                "This Result reports failure but carries no Error — it is likely a " +
                "default(Result) rather than one constructed via Result.Failure().");
        }
    }

    public static Result Success() => new(true, null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(false, error);
    }

    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>Reduces the result to a single value without branching at the call site.</summary>
    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess() : onFailure(Error);
    }

    public bool Equals(Result other) => IsSuccess == other.IsSuccess && Equals(_error, other._error);

    public override bool Equals(object? obj) => obj is Result other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(IsSuccess, _error);

    public static bool operator ==(Result left, Result right) => left.Equals(right);

    public static bool operator !=(Result left, Result right) => !left.Equals(right);
}
