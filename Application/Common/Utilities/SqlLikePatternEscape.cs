namespace Application.Common.Utilities;

public static class SqlLikePatternEscape
{
    public static string EscapeForSqlServerLike(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value
            .Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
    }
}
