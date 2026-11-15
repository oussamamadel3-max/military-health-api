using System.Data;
using Npgsql;

namespace Application.Tests;

/// <summary>
/// Phase C.1: بوابة تكامل PostgreSQL المعزول لصلاحيات الحقول.
/// عند توفر <c>MHS_EXPORT_EF_REVIEW_PG</c> (قاعدة <c>mhs_export_ef_review</c> فقط) يتحقق الاتصال؛
/// وإلا يُتخطى دون فشل — لا يُلمس إنتاج ولا قواعد مشتركة أخرى.
/// </summary>
public class MemberFieldPermissionPostgresIntegrationTests
{
    private const string RequiredDatabase = "mhs_export_ef_review";

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC1")]
    public async Task Postgres_review_database_is_reachable_when_configured()
    {
        var connectionString = ResolveConnectionString();
        if (connectionString is null)
            return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        Assert.Equal(ConnectionState.Open, connection.State);

        await using var cmd = new NpgsqlCommand("SELECT current_database()", connection);
        var dbName = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal(RequiredDatabase, dbName);
    }

    private static string? ResolveConnectionString()
    {
        var cs = Environment.GetEnvironmentVariable("MHS_EXPORT_EF_REVIEW_PG");
        if (!string.IsNullOrWhiteSpace(cs))
        {
            var builder = new NpgsqlConnectionStringBuilder(cs.Trim());
            if (!string.Equals(builder.Database, RequiredDatabase, StringComparison.OrdinalIgnoreCase))
                return null;
            return builder.ConnectionString;
        }

        var pwdPath = Path.Combine(Path.GetTempPath(), "mhs_review_tester_pwd.txt");
        if (!File.Exists(pwdPath))
            return null;

        var pwd = File.ReadAllText(pwdPath).Trim();
        return $"Host=127.0.0.1;Port=5432;Database={RequiredDatabase};Username=mhs_review_tester;Password={pwd}";
    }
}
