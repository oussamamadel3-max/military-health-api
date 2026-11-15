using Application.Services.AuthServices;
using Domain.Enums;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Application.Tests;

public class HttpUserContextServiceTests
{
    [Fact]
    public void RoleIds_come_from_role_id_claims_not_role_names()
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "u1"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "SuperAdmin"));
        identity.AddClaim(new Claim(HttpUserContextService.RoleIdClaimType, "role-guid-1"));
        identity.AddClaim(new Claim("field_permission", "Probe:SecretValue:Read"));

        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var accessor = new HttpContextAccessor { HttpContext = http };
        var ctx = new HttpUserContextService(accessor);

        Assert.Equal(["role-guid-1"], ctx.RoleIds);
        Assert.Equal(["SuperAdmin"], ctx.RoleNames);
        Assert.Equal(FieldAccessType.Read, ctx.FieldPermissions["Probe:SecretValue"]);
    }
}
