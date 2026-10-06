using System.Reflection;
using System.Text;

namespace Infrastructure.Persistence.Sql;

internal static class InitialDatabaseObjects
{
    public const string Extensions = """
        CREATE EXTENSION IF NOT EXISTS citext;
        CREATE EXTENSION IF NOT EXISTS pgcrypto;
        """;

    public static IReadOnlyList<string> UpStatements { get; } = Split(Read("InitialDatabaseObjects.Up.sql"));

    public static IReadOnlyList<string> DownStatements { get; } = Split(Read("InitialDatabaseObjects.Down.sql"));

    public static IReadOnlyList<string> ExtensionStatements { get; } = Split(Extensions);

    /// <summary>
    /// Splits a script on semicolons that are outside quotes, comments, and dollar-quoted function bodies.
    /// Npgsql executes one statement per migration command.
    /// </summary>
    public static IReadOnlyList<string> Split(string sql)
    {
        var statements = new List<string>();
        var current = new StringBuilder();
        string? dollarTag = null;
        var lineComment = false;
        var blockComment = false;
        var singleQuote = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (dollarTag != null)
            {
                if (sql.AsSpan(i).StartsWith(dollarTag))
                {
                    current.Append(dollarTag);
                    i += dollarTag.Length - 1;
                    dollarTag = null;
                }
                else
                {
                    current.Append(c);
                }
                continue;
            }

            if (lineComment)
            {
                current.Append(c);
                if (c == '\n')
                    lineComment = false;
                continue;
            }

            if (blockComment)
            {
                current.Append(c);
                if (c == '*' && next == '/')
                {
                    current.Append(next);
                    i++;
                    blockComment = false;
                }
                continue;
            }

            if (singleQuote)
            {
                current.Append(c);
                if (c == '\'')
                {
                    if (next == '\'')
                    {
                        current.Append(next);
                        i++;
                    }
                    else
                    {
                        singleQuote = false;
                    }
                }
                continue;
            }

            if (c == '-' && next == '-')
            {
                lineComment = true;
                current.Append(c);
                continue;
            }

            if (c == '/' && next == '*')
            {
                blockComment = true;
                current.Append(c);
                continue;
            }

            if (c == '\'')
            {
                singleQuote = true;
                current.Append(c);
                continue;
            }

            if (c == '$')
            {
                var end = i + 1;
                while (end < sql.Length && (char.IsLetterOrDigit(sql[end]) || sql[end] == '_'))
                    end++;
                if (end < sql.Length && sql[end] == '$')
                {
                    dollarTag = sql.Substring(i, end - i + 1);
                    current.Append(dollarTag);
                    i = end;
                    continue;
                }
            }

            if (c == ';')
            {
                var statement = current.ToString().Trim();
                if (statement.Length > 0)
                    statements.Add(statement);
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        var tail = current.ToString().Trim();
        if (tail.Length > 0)
            statements.Add(tail);
        return statements;
    }

    private static string Read(string fileName)
    {
        var assembly = typeof(InitialDatabaseObjects).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded SQL resource {fileName}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
