namespace API.IntegrationTests.Infrastructure;

/// <summary>
/// MH-SEC-091: isolated WAF instance so closure tests do not mutate the MH-SEC-051 baseline database.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AuthHttp091Collection : ICollectionFixture<AuthWebApplicationFactory>
{
    public const string Name = "AuthHttp091";
}
