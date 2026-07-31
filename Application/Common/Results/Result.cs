namespace Application.Common.Results;

public class Result
{
    internal Result(bool succeeded, IEnumerable<string>? errors, IEnumerable<string>? warnings = null)
    {
        Succeeded = succeeded;
        Errors = errors?.ToArray() ?? Array.Empty<string>();
        Warnings = warnings?.ToArray() ?? Array.Empty<string>();
    }

    public bool Succeeded { get; init; }
    public bool Failed => !Succeeded;

    public string[] Errors { get; init; }
    public string[] Warnings { get; init; }

    public static Result Success(IEnumerable<string>? warnings = null)
        => new(true, null, warnings);

    public static Result Failure(IEnumerable<string> errors, IEnumerable<string>? warnings = null)
        => new(false, errors, warnings);
}
