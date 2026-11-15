using Microsoft.AspNetCore.Hosting;

namespace API.IntegrationTests.Infrastructure;

/// <summary>
/// مصنع معزول لمرحلة C مع <c>FieldPermissions:EnforcementEnabled=true</c>
/// دون تغيير المصنع الافتراضي (العلم معطّل).
/// </summary>
public sealed class FieldPermissionsEnforcementWebApplicationFactory : AuthWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("FieldPermissions:EnforcementEnabled", "true");
    }
}

[CollectionDefinition(Name)]
public sealed class FieldPermissionsEnforcementHttpCollection
    : ICollectionFixture<FieldPermissionsEnforcementWebApplicationFactory>
{
    public const string Name = "FieldPermissionsEnforcementHttp";
}
