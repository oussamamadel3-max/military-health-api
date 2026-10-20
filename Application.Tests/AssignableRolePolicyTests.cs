using Application.Common.Security;
using Application.Exceptions;
using Domain.Enums;

namespace Application.Tests;

public class AssignableRolePolicyTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Theory]
    [InlineData("ReceptionClerk")]
    [InlineData("ClinicDoctor")]
    [InlineData("Supervisor")]
    [InlineData("ArchiveClerk")]
    public void Local_actor_may_assign_approved_operational_roles(string roleName)
    {
        AssignableRolePolicy.EnsureAssignable(actorIsCentral: false, roleName, roleIsActive: true, RoleScope.Center);
        var scope = AssignableRolePolicy.ResolveSyncScope(false, [RoleScope.Center], null, CenterA, false);
        Assert.Equal(CenterA, scope);
    }

    [Theory]
    [InlineData("SuperAdmin", RoleScope.Central)]
    [InlineData("Auditor", RoleScope.Central)]
    [InlineData("CenterManager", RoleScope.Center)]
    [InlineData("CenterOfficer", RoleScope.Center)]
    [InlineData("CustomRole", RoleScope.Center)]
    public void Local_actor_cannot_assign_unlisted_or_central_roles(string roleName, RoleScope roleScope)
    {
        Assert.Throws<ForbiddenPermissionException>(() =>
            AssignableRolePolicy.EnsureAssignable(actorIsCentral: false, roleName, roleIsActive: true, roleScope));
    }

    [Fact]
    public void Inactive_role_is_rejected_before_scope()
    {
        Assert.Throws<InvalidUserDataException>(() =>
            AssignableRolePolicy.EnsureAssignable(actorIsCentral: true, "ReceptionClerk", roleIsActive: false, RoleScope.Center));
    }

    [Fact]
    public void Central_actor_must_bind_operational_role_to_a_center()
    {
        Assert.Throws<InvalidUserDataException>(() =>
            AssignableRolePolicy.ResolveSyncScope(true, [RoleScope.Center], null, null, true));

        var scope = AssignableRolePolicy.ResolveSyncScope(true, [RoleScope.Center], CenterA, null, true);
        Assert.Equal(CenterA, scope);
    }

    [Fact]
    public void Central_catalog_role_stays_unscoped_only_for_central_actor()
    {
        Assert.Null(AssignableRolePolicy.ResolveSyncScope(true, [RoleScope.Central], null, null, true));
        Assert.Throws<InvalidUserDataException>(() =>
            AssignableRolePolicy.ResolveSyncScope(true, [RoleScope.Central], CenterA, null, true));
    }

    [Fact]
    public void Custom_central_role_uses_stored_scope_not_name()
    {
        Assert.Null(AssignableRolePolicy.ResolveSyncScope(true, [RoleScope.Central], null, null, true));
        Assert.Throws<InvalidUserDataException>(() =>
            AssignableRolePolicy.ResolveSyncScope(true, [RoleScope.Central], CenterA, null, true));
    }

    [Fact]
    public void Local_actor_cannot_target_another_center()
    {
        Assert.Throws<ForbiddenPermissionException>(() =>
            AssignableRolePolicy.ResolveSyncScope(false, [RoleScope.Center], CenterB, CenterA, false));
    }

    [Fact]
    public void Mixed_central_and_scoped_roles_are_rejected()
    {
        Assert.Throws<InvalidUserDataException>(() =>
            AssignableRolePolicy.ResolveSyncScope(true, [RoleScope.Central, RoleScope.Center], CenterA, null, true));
    }

    [Fact]
    public void Local_actor_may_revoke_only_operational_roles_in_scope()
    {
        Assert.True(AssignableRolePolicy.MayRevoke(false, "ClinicDoctor", CenterA, RoleScope.Center));
        Assert.False(AssignableRolePolicy.MayRevoke(false, "CenterManager", CenterA, RoleScope.Center));
        Assert.False(AssignableRolePolicy.MayRevoke(false, "SuperAdmin", null, RoleScope.Central));
        Assert.True(AssignableRolePolicy.MayRevoke(true, "ReceptionClerk", CenterA, RoleScope.Center));
        Assert.False(AssignableRolePolicy.MayRevoke(true, "ReceptionClerk", null, RoleScope.Center));
        Assert.True(AssignableRolePolicy.MayRevoke(true, "Auditor", null, RoleScope.Central));
    }
}
