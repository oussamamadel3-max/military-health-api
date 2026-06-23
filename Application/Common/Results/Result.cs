namespace Application.Common.Results;


public class Result
{
    internal Result(bool succeeded, IEnumerable<string>? errors = null, IEnumerable<string>? warnings = null)
    {
        Succeeded = succeeded;
        Errors = errors?.ToArray() ?? Array.Empty<string>();
        Warnings = warnings?.ToArray() ?? Array.Empty<string>();
    }

    public bool Succeeded { get; init; }
    public bool Failed => !Succeeded;

    public string[] Errors { get; init; }
    public string[] Warnings { get; init; }

    // Success بدون قيمة
    public static Result Success(IEnumerable<string>? warnings = null)
        => new Result(true, warnings: warnings);

    // Failure بدون قيمة
    public static Result Failure(IEnumerable<string> errors, IEnumerable<string>? warnings = null)
        => new Result(false, errors, warnings);
}

