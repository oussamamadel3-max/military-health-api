using API.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Application.Tests;

public class ConfigurationSecretsExtensionsTests
{
    [Fact]
    public void Production_missing_secrets_throws_without_leaking_values()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var env = new HostingEnvironment { EnvironmentName = Environments.Production };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationSecretsExtensions.ValidateRequiredSecrets(config, env));

        Assert.Contains("ConnectionStrings__DefaultConnection", ex.Message, StringComparison.Ordinal);
        Assert.Contains("JwtSettings__Key", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_missing_secrets_points_to_local_example()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var env = new HostingEnvironment { EnvironmentName = Environments.Development };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationSecretsExtensions.ValidateRequiredSecrets(config, env));

        Assert.Contains("appsettings.Local.example.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains(ConfigurationSecretsExtensions.LocalSettingsFileName, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Testing_skips_validation()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new HostingEnvironment { EnvironmentName = "Testing" };

        ConfigurationSecretsExtensions.ValidateRequiredSecrets(config, env);
    }

    private sealed class HostingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
