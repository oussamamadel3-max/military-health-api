using Microsoft.AspNetCore.Hosting;

namespace API.IntegrationTests.Infrastructure;

/// <summary>
/// مصنع معزول لمرحلة C.1 مع الإنفاذ معطّلاً صراحةً (قاعدة منفصلة عن كتالوج Phase B).
/// </summary>
public sealed class FieldPermissionsEnforcementOffWebApplicationFactory : AuthWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("FieldPermissions:EnforcementEnabled", "false");
    }
}

[CollectionDefinition(Name)]
public sealed class FieldPermissionsEnforcementOffHttpCollection
    : ICollectionFixture<FieldPermissionsEnforcementOffWebApplicationFactory>
{
    public const string Name = "FieldPermissionsEnforcementOffHttp";
}
