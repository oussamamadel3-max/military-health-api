using API.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace API.IntegrationTests.RateLimiting;

public class RateLimitSettingsTests
{
    [Theory]
    [InlineData("appsettings.Development.json")]
    [InlineData("appsettings.Production.json")]
    public void Development_and_production_inherit_the_base_rate_limits(string environmentFile)
    {
        var settings = Bind(environmentFile);
        var result = new RateLimitingSettingsValidator().Validate(null, settings);
        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? []));

        Assert.Equal(5, settings.Login.PermitLimit);
        Assert.Equal(60, settings.Login.WindowSeconds);
        Assert.Equal(5, settings.Refresh.PermitLimit);
        Assert.Equal(60, settings.Refresh.WindowSeconds);
        Assert.Equal(2, settings.Reports.ConcurrencyLimit);
        Assert.Equal(10, settings.Reports.TokenLimit);
        Assert.Equal(5, settings.Reports.TokensPerPeriod);
        Assert.Equal(120, settings.Global.Authenticated.TokenLimit);
        Assert.Equal(60, settings.Global.Authenticated.TokensPerPeriod);
        Assert.Equal(30, settings.Global.Anonymous.TokenLimit);
        Assert.Equal(160, settings.Safety.Authenticated.TokenLimit);
        Assert.Equal(90, settings.Safety.Authenticated.TokensPerPeriod);
        Assert.Equal(60, settings.Safety.Anonymous.TokenLimit);
        Assert.Equal(4, settings.HeavyRead.ConcurrencyLimit);
        Assert.Equal(24, settings.HeavyRead.TokenLimit);
        Assert.Equal(12, settings.HeavyRead.TokensPerPeriod);
        Assert.Equal(40, settings.Administrative.TokenLimit);
        Assert.Equal(30, settings.Administrative.TokensPerPeriod);
        Assert.Equal(48, settings.ReferenceData.TokenLimit);
        Assert.Equal(30, settings.ReferenceData.TokensPerPeriod);
    }

    [Fact]
    public void Testing_settings_stay_stricter_than_login_and_still_validate()
    {
        var settings = Bind("appsettings.Testing.json");
        var result = new RateLimitingSettingsValidator().Validate(null, settings);
        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? []));
        Assert.True(settings.Login.PermitLimit is > 0 and <= 5);
        Assert.True(settings.Refresh.PermitLimit is > 0 and <= 5);
        Assert.True(settings.Reports.TokenLimit is > 0 and <= 10);
        Assert.True(settings.Reports.ConcurrencyLimit > 0);
        Assert.True(settings.Safety.Authenticated.TokenLimit > 0);
        Assert.True(settings.HeavyRead.TokenLimit > 0);
        Assert.True(settings.Administrative.TokenLimit > 0);
        Assert.True(settings.ReferenceData.TokenLimit > 0);
    }

    [Fact]
    public void Sensitive_user_operations_are_not_moved_to_the_administrative_policy()
    {
        var users = File.ReadAllText(Path.Combine(ApiDirectory(), "Controllers", "AuthControllers", "UserController.cs"));
        Assert.DoesNotContain("EnableRateLimiting", users, StringComparison.Ordinal);
        Assert.Contains("ChangeMyPassword", users, StringComparison.Ordinal);
        Assert.Contains("ChangePasswordUser", users, StringComparison.Ordinal);
        Assert.Contains("UpdateUserStatus", users, StringComparison.Ordinal);
        Assert.Contains("ToggleRolesForUserAsync", users, StringComparison.Ordinal);
        Assert.Contains("SyncDirectPermissions", users, StringComparison.Ordinal);
        Assert.Contains("RemoveFieldPermissionsFromUser", users, StringComparison.Ordinal);

        var auth = File.ReadAllText(Path.Combine(ApiDirectory(), "Controllers", "AuthControllers", "AuthController.cs"));
        Assert.Contains("[EnableRateLimiting(RateLimitPolicies.Login)]", auth, StringComparison.Ordinal);
        Assert.Contains("[EnableRateLimiting(RateLimitPolicies.Refresh)]", auth, StringComparison.Ordinal);

        var fields = File.ReadAllText(Path.Combine(ApiDirectory(), "Controllers", "AuthControllers", "FieldPermissionsController.cs"));
        Assert.Contains("public async Task<IResult> Create", fields, StringComparison.Ordinal);
        Assert.DoesNotContain("Administrative)]\r\n        public async Task<IResult> Create", fields, StringComparison.Ordinal);
        Assert.DoesNotContain("Administrative)]\n        public async Task<IResult> Create", fields, StringComparison.Ordinal);
        Assert.DoesNotContain("Administrative)]\r\n        public async Task<IResult> Update", fields, StringComparison.Ordinal);
        Assert.DoesNotContain("Administrative)]\n        public async Task<IResult> Update", fields, StringComparison.Ordinal);
        Assert.DoesNotContain("Administrative)]\r\n        public async Task<IResult> Delete", fields, StringComparison.Ordinal);
        Assert.DoesNotContain("Administrative)]\n        public async Task<IResult> Delete", fields, StringComparison.Ordinal);
        Assert.DoesNotContain("Administrative)]\r\n        public async Task<IResult> ChangeAccessType", fields, StringComparison.Ordinal);
        Assert.DoesNotContain("Administrative)]\n        public async Task<IResult> ChangeAccessType", fields, StringComparison.Ordinal);
    }

    private static RateLimitingSettings Bind(string environmentFile)
    {
        var directory = ApiDirectory();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(directory, "appsettings.json"))
            .AddJsonFile(Path.Combine(directory, environmentFile))
            .Build();

        var settings = new RateLimitingSettings();
        configuration.GetSection(RateLimitingSettings.SectionName).Bind(settings);
        return settings;
    }

    private static string ApiDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "API", "appsettings.json");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "API");
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate API/appsettings.json.");
    }
}
