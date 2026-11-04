namespace API.Extensions;

/// <summary>
/// FR-080 / MH-FIX-080: validate required secrets without logging values.
/// </summary>
public static class ConfigurationSecretsExtensions
{
    public const string LocalSettingsFileName = "appsettings.Local.json";

    public static void ValidateRequiredSecrets(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsEnvironment("Testing"))
            return;

        var missingConnection = IsMissingSecret(configuration.GetConnectionString("DefaultConnection"));
        var missingJwtKey = IsMissingSecret(configuration.GetSection("JwtSettings").GetValue<string>("Key"));

        if (!missingConnection && !missingJwtKey)
            return;

        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Production configuration is incomplete. Set ConnectionStrings__DefaultConnection and JwtSettings__Key " +
                "via environment variables or the host secret store. Secret values must not be committed to Git or written to logs.");
        }

        if (environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Development configuration is incomplete. Copy API/appsettings.Local.example.json to " +
                $"{LocalSettingsFileName}, set ConnectionStrings:DefaultConnection and JwtSettings:Key locally " +
                "(the local file is gitignored), or provide the same keys via user secrets or environment variables.");
        }

        throw new InvalidOperationException(
            "Application configuration is incomplete. Provide ConnectionStrings:DefaultConnection and JwtSettings:Key " +
            "via environment-specific settings or environment variables.");
    }

    private static bool IsMissingSecret(string? value) => string.IsNullOrWhiteSpace(value);
}
