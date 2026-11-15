using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace API.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the API with the RateLimiting section from appsettings.json.
/// The base factory raises functional-suite budgets; this type puts the real numbers back.
/// </summary>
public sealed class ConfiguredRateLimitWebApplicationFactory : AuthWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        var path = Path.Combine(FindApiProjectDirectory(), "appsettings.json");
        var configured = new ConfigurationBuilder().AddJsonFile(path).Build();
        foreach (var pair in configured.AsEnumerable())
        {
            if (pair.Key.StartsWith("RateLimiting:", StringComparison.Ordinal) && pair.Value is not null)
                builder.UseSetting(pair.Key, pair.Value);
        }
    }
}
