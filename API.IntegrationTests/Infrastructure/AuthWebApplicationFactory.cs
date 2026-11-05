using Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.IntegrationTests.Infrastructure;

/// <summary>
/// MH-SEC-051: hosts the real API pipeline against an isolated SQLite file — never Dev/Prod connection strings.
/// </summary>
public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"mh-sec-051-{Guid.NewGuid():N}.sqlite");

    private SqliteConnection? _connection;

    public string DatabasePath => _dbPath;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync();
        _connection.CreateFunction("btrim", (string? value) => value?.Trim() ?? string.Empty);

        // Force host creation so schema + seed run before first test.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await SqliteSchemaBootstrap.EnsureCreatedAsync(db);
        await AuthTestSeed.SeedAsync(scope.ServiceProvider);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();

        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
            // Best-effort cleanup of temp SQLite file.
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting applies early enough for top-level Program service registration (JWT key, etc.).
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_dbPath}");
        builder.UseSetting("JwtSettings:Key", "MH-SEC-051-integration-test-signing-key-32chars!");
        builder.UseSetting("JwtSettings:Issuer", "https://localhost/mh-sec-051");
        builder.UseSetting("JwtSettings:Audience", "mh-sec-051-tests");
        builder.UseSetting("JwtSettings:ExpirationInMinutes", "30");
        builder.UseSetting("JwtSettings:RefreshTokenExpirationInDays", "2");
        builder.UseSetting("RateLimiting:Login:PermitLimit", "100");
        builder.UseSetting("RateLimiting:Login:WindowSeconds", "60");
        builder.UseSetting("RateLimiting:Login:SegmentsPerWindow", "6");
        // MH-SEC-055 / D3 Testing budget (2/60s). Sessions use unique X-Forwarded-For to avoid cross-test bleed.
        builder.UseSetting("RateLimiting:Refresh:PermitLimit", "2");
        builder.UseSetting("RateLimiting:Refresh:WindowSeconds", "60");
        builder.UseSetting("RateLimiting:Refresh:SegmentsPerWindow", "6");
        builder.UseSetting("RateLimiting:Global:Authenticated:TokenLimit", "1000");
        builder.UseSetting("RateLimiting:Global:Authenticated:TokensPerPeriod", "1000");
        builder.UseSetting("RateLimiting:Global:Anonymous:TokenLimit", "1000");
        builder.UseSetting("RateLimiting:Global:Anonymous:TokensPerPeriod", "1000");
        builder.UseSetting("FileStorage:RootPath", Path.Combine(Path.GetTempPath(), "mh-sec-051-storage"));
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost");

        // ConfigureTestServices runs after Program DI — required to replace Npgsql with SQLite cleanly.
        builder.ConfigureTestServices(services =>
        {
            RemoveDbContextRegistrations(services);

            services.AddDbContext<AppDbContext>((_, options) =>
            {
                options.UseSqlite(_connection ?? throw new InvalidOperationException("SQLite connection not opened."));
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseContentRoot(FindApiProjectDirectory());
        return base.CreateHost(builder);
    }

    public HttpClient CreateCookieClient()
        => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    public AuthHttpSession CreateSession() => new(this);

    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(db);
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(db);
    }

    private static void RemoveDbContextRegistrations(IServiceCollection services)
    {
        var descriptors = services
            .Where(d =>
            {
                var typeName = d.ServiceType.FullName ?? string.Empty;
                if (d.ServiceType == typeof(AppDbContext)
                    || d.ServiceType == typeof(DbContextOptions)
                    || d.ServiceType == typeof(DbContextOptions<AppDbContext>))
                    return true;

                if (d.ServiceType.IsGenericType)
                {
                    var def = d.ServiceType.GetGenericTypeDefinition();
                    if (def == typeof(DbContextOptions<>)
                        && d.ServiceType.GenericTypeArguments[0] == typeof(AppDbContext))
                        return true;
                }

                // EF Core 8+ keeps provider configuration via IDbContextOptionsConfiguration<T>.
                if (typeName.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal)
                    && typeName.Contains(nameof(AppDbContext), StringComparison.Ordinal))
                    return true;

                if (d.ImplementationType == typeof(AppDbContext))
                    return true;

                return false;
            })
            .ToList();

        foreach (var descriptor in descriptors)
            services.Remove(descriptor);
    }

    private static string FindApiProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var api = Path.Combine(dir.FullName, "API", "API.csproj");
            if (File.Exists(api))
                return Path.Combine(dir.FullName, "API");
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate API project directory for WebApplicationFactory content root.");
    }
}

[CollectionDefinition(Name)]
public sealed class AuthHttpCollection : ICollectionFixture<AuthWebApplicationFactory>
{
    public const string Name = "AuthHttp";
}
