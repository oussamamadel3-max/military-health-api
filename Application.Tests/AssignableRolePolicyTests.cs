using Application.Common.Security;
using Application.Exceptions;

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
        AssignableRolePolicy.EnsureAssignable(actorIsCentral: false, roleName, roleIsActive: true);
        var scope = AssignableRolePolicy.ResolveSyncScope(false, [roleName], null, CenterA, false);
        Assert.Equal(CenterA, scope);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("Auditor")]
    [InlineData("CenterManager")]
    [InlineData("CenterOfficer")]
    [InlineData("CustomRole")]
    public void Local_actor_cannot_assign_unlisted_roles(string roleName)
    {
        Assert.Throws<ForbiddenPermissionException>(() =>
            AssignableRolePolicy.EnsureAssignable(actorIsCentral: false, roleName, roleIsActive: true));
    }

    [Fact]
    public void Inactive_role_is_rejected_before_scope()
    {
        Assert.Throws<InvalidUserDataException>(() =>
            AssignableRolePolicy.EnsureAssignable(actorIsCentral: true, "ReceptionClerk", roleIsActive: false));
    }

    [Fact]
    public void Central_actor_must_bind_operational_role_to_a_center()
    {
        Assert.Throws<InvalidUserDataException>(() =>
            AssignableRolePolicy.ResolveSyncScope(true, ["ClinicDoctor"], null, null, true));

        var scope = AssignableRolePolicy.ResolveSyncScope(true, ["ClinicDoctor"], CenterA, null, true);
        Assert.Equal(CenterA, scope);
    }

    [Fact]
    public void Central_catalog_role_stays_unscoped_only_for_central_actor()
    {
        Assert.Null(AssignableRolePolicy.ResolveSyncScope(true, ["SuperAdmin"], null, null, true));
        Assert.Throws<InvalidUserDataException>(() =>
            AssignableRolePolicy.ResolveSyncScope(true, ["Auditor"], CenterA, null, true));
    }

    [Fact]
    public void Local_actor_cannot_target_another_center()
    {
        Assert.Throws<ForbiddenPermissionException>(() =>
            AssignableRolePolicy.ResolveSyncScope(false, ["ReceptionClerk"], CenterB, CenterA, false));
    }

    [Fact]
    public void Mixed_central_and_scoped_roles_are_rejected()
    {
        Assert.Throws<InvalidUserDataException>(() =>
            AssignableRolePolicy.ResolveSyncScope(true, ["SuperAdmin", "ReceptionClerk"], CenterA, null, true));
    }

    [Fact]
    public void Local_actor_may_revoke_only_operational_roles_in_scope()
    {
        Assert.True(AssignableRolePolicy.MayRevoke(false, "ClinicDoctor", CenterA));
        Assert.False(AssignableRolePolicy.MayRevoke(false, "CenterManager", CenterA));
        Assert.False(AssignableRolePolicy.MayRevoke(false, "SuperAdmin", null));
        Assert.True(AssignableRolePolicy.MayRevoke(true, "ReceptionClerk", CenterA));
        Assert.False(AssignableRolePolicy.MayRevoke(true, "ReceptionClerk", null));
        Assert.True(AssignableRolePolicy.MayRevoke(true, "Auditor", null));
    }
}
