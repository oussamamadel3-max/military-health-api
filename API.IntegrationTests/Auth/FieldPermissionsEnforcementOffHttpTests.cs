using System.Net;
using API.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace API.IntegrationTests.Auth;

/// <summary>
/// Phase C.1: مع <c>EnforcementEnabled=false</c> يظهر MotherName حتى بلا صلاحية حقل فعّالة.
/// </summary>
[Collection(FieldPermissionsEnforcementOffHttpCollection.Name)]
public sealed class FieldPermissionsEnforcementOffHttpTests
{
    private readonly FieldPermissionsEnforcementOffWebApplicationFactory _factory;

    public FieldPermissionsEnforcementOffHttpTests(FieldPermissionsEnforcementOffWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC1")]
    public async Task Disabled_enforcement_returns_MotherName_without_field_grant()
    {
        await FieldPermissionsEnforcementHttpSeed.EnsureAsync(_factory.Services);

        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK,
            (await session.LoginAsync(
                FieldPermissionsEnforcementHttpSeed.NoneUserName,
                FieldPermissionsEnforcementHttpSeed.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await session.SelectCenterAsync(FieldPermissionsEnforcementHttpSeed.CenterB)).StatusCode);

        var response = await session.Client.GetAsync(
            $"/api/members/{FieldPermissionsEnforcementHttpSeed.SeededMemberId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await session.ReadJsonAsync(response);
        Assert.Equal(
            FieldPermissionsEnforcementHttpSeed.SeededMotherName,
            doc.RootElement.GetProperty("data").GetProperty("motherName").GetString());

        var stored = await _factory.WithDbAsync(db =>
            db.Members.AsNoTracking()
                .Where(m => m.MemberId == FieldPermissionsEnforcementHttpSeed.SeededMemberId)
                .Select(m => m.MotherName)
                .SingleAsync());
        Assert.Equal(FieldPermissionsEnforcementHttpSeed.SeededMotherName, stored);
    }
}
