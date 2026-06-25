namespace Application.Common.Results;

public class Result<T> : Result
{
    internal Result(bool succeeded, IEnumerable<string>? errors, T? value, IEnumerable<string>? warnings = null)
        : base(succeeded, errors, warnings)
    {
        Value = value;
    }

    public T? Value { get; init; }

    public static Result<T> Success(T value, IEnumerable<string>? warnings = null)
        => new(true, null, value, warnings);

    public static Result<T> Failure(IEnumerable<string> errors, T? value = default!, IEnumerable<string>? warnings = null)
        => new(false, errors, value, warnings);
}
