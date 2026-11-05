using System.Text.RegularExpressions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace API.IntegrationTests.Infrastructure;

/// <summary>
/// Builds a SQLite schema from EF's create script with PostgreSQL cast tokens stripped
/// (same approach as Application.Tests AuditAndModelTests).
/// </summary>
internal static class SqliteSchemaBootstrap
{
    public static async Task EnsureCreatedAsync(AppDbContext db)
    {
        var script = db.Database.GenerateCreateScript();
        script = Regex.Replace(script, "::[A-Za-z_][A-Za-z0-9_]*", string.Empty);

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(statement))
                continue;

            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            try
            {
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"SQLite schema statement failed:{Environment.NewLine}{statement}", ex);
            }
        }
    }
}
