using Application.Common.Permissions;
using Application.Exceptions;
using Application.Services.AuthServices;
using Microsoft.AspNetCore.Http;
using Moq;
using System.Security.Claims;

namespace Application.Tests;

public class PermissionGateTests
{
    private static PermissionGate CreateGate(params string[] permissions)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var p in permissions)
            identity.AddClaim(new Claim("permissions", p));
        // Role name alone must not grant access
        identity.AddClaim(new Claim(ClaimTypes.Role, "SuperAdmin"));

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(http);
        return new PermissionGate(accessor.Object);
    }

    [Fact]
    public void Has_succeeds_with_permission_even_without_relying_on_SuperAdmin_role()
    {
        var gate = CreateGate(SystemPermissions.UserManagement.Read);
        Assert.True(gate.Has(SystemPermissions.UserManagement.Read));
        Assert.False(gate.Has(SystemPermissions.UserManagement.Delete));
    }

    [Fact]
    public void EnsureAll_rejects_SuperAdmin_role_without_required_permission()
    {
        var gate = CreateGate(SystemPermissions.UserManagement.Edit);
        Assert.Throws<ForbiddenPermissionException>(() =>
            gate.EnsureAll(SystemPermissions.UserManagement.ChangeStatus));
    }

    [Fact]
    public void EnsureAll_accepts_when_all_permissions_present_without_SuperAdmin_role_claim_needed()
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("permissions", SystemPermissions.UserManagement.Create));
        identity.AddClaim(new Claim("permissions", SystemPermissions.UserManagement.AssignRoles));
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(http);
        var gate = new PermissionGate(accessor.Object);

        gate.EnsureAll(
            SystemPermissions.UserManagement.Create,
            SystemPermissions.UserManagement.AssignRoles);
    }

    [Fact]
    public void Edit_alone_does_not_satisfy_sensitive_user_ops()
    {
        var gate = CreateGate(SystemPermissions.UserManagement.Edit);
        Assert.False(gate.HasAll(SystemPermissions.UserManagement.ChangeStatus));
        Assert.False(gate.HasAll(SystemPermissions.UserManagement.AssignRoles));
        Assert.False(gate.HasAll(SystemPermissions.UserManagement.AssignPermissions));
        Assert.False(gate.HasAll(SystemPermissions.UserManagement.ResetPassword));
    }

    [Fact]
    public void Export_permissions_are_scoped_to_audit_and_dashboard_modules()
    {
        var auditExport = CreateGate(SystemPermissions.AuditLog.Export);
        Assert.True(auditExport.Has(SystemPermissions.AuditLog.Export));
        Assert.False(auditExport.Has(SystemPermissions.Dashboard.Export));

        var dashExport = CreateGate(SystemPermissions.Dashboard.Export);
        Assert.True(dashExport.Has(SystemPermissions.Dashboard.Export));
    }
}

public class IamAdminGuardTests
{
    [Fact]
    public void IsIamAdministrator_requires_write_in_users_roles_and_permissions()
    {
        Assert.False(IamAdminGuard.IsIamAdministrator(new[]
        {
            SystemPermissions.UserManagement.Edit,
            SystemPermissions.RoleManagement.Edit,
        }));

        Assert.True(IamAdminGuard.IsIamAdministrator(new[]
        {
            SystemPermissions.UserManagement.AssignRoles,
            SystemPermissions.RoleManagement.AssignPermissions,
            SystemPermissions.PermissionManagement.ChangeStatus,
        }));
    }

    [Fact]
    public void EnsureAssignableWithinActorPermissions_blocks_escalation()
    {
        Assert.Throws<ForbiddenPermissionException>(() =>
            IamAdminGuard.EnsureAssignableWithinActorPermissions(
                new[] { SystemPermissions.UserManagement.Delete },
                new[] { SystemPermissions.UserManagement.Read }));
    }

    [Fact]
    public void EnsureAssignableWithinActorPermissions_allows_subset()
    {
        IamAdminGuard.EnsureAssignableWithinActorPermissions(
            new[] { SystemPermissions.UserManagement.Read },
            new[]
            {
                SystemPermissions.UserManagement.Read,
                SystemPermissions.UserManagement.Edit,
            });
    }
}

public class AuthorizePermissionAttributeTests
{
    [Fact]
    public void Attribute_requires_all_listed_permissions()
    {
        var identity = new ClaimsIdentity("Bearer", ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim("permissions", SystemPermissions.UserManagement.Create));
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(http);
        var gate = new PermissionGate(accessor.Object);

        Assert.Throws<ForbiddenPermissionException>(() =>
            gate.EnsureAll(SystemPermissions.UserManagement.Create, SystemPermissions.UserManagement.AssignRoles));

        identity.AddClaim(new Claim("permissions", SystemPermissions.UserManagement.AssignRoles));
        http.User = new ClaimsPrincipal(identity);
        gate.EnsureAll(SystemPermissions.UserManagement.Create, SystemPermissions.UserManagement.AssignRoles);
    }
}
