using ArchiveProject.Application.Common.Results;

public class Result<T> : Result
{
    internal Result(bool succeeded, IEnumerable<string>? errors, T? value, IEnumerable<string>? warnings = null)
        : base(succeeded, errors, warnings)
    {
        Value = value;
    }

    public T? Value { get; set; }

    // ✅ Success مع قيمة
    public static Result<T> Success(T value, IEnumerable<string>? warnings = null)
        => new Result<T>(true, null, value, warnings);

    // ✅ Failure مع أخطاء (وقيمة اختيارية)
    public static Result<T> Failure(IEnumerable<string> errors, T? value = default!, IEnumerable<string>? warnings = null)
        => new Result<T>(false, errors, value, warnings);
}

