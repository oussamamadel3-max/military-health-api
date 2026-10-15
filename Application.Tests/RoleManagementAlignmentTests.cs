using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Entities.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;

namespace Application.Tests;

public class RoleManagementAlignmentTests
{
    [Fact]
    public void Reserved_roles_match_srs_eight_base_roles()
    {
        Assert.Equal(8, MilitaryHealthSeedCatalog.Roles.Length);
        foreach (var role in MilitaryHealthSeedCatalog.Roles)
            Assert.True(ReservedRoleGuard.IsReserved(role.Name));

        Assert.True(ReservedRoleGuard.IsCentralAdminRole("SuperAdmin"));
        Assert.False(ReservedRoleGuard.IsReserved("CustomOps"));
    }

    [Fact]
    public void ReservedRoleGuard_blocks_rename_of_seed_roles()
    {
        Assert.Throws<ForbiddenPermissionException>(() =>
            ReservedRoleGuard.EnsureCanRename("CenterManager", "LocalManager"));

        ReservedRoleGuard.EnsureCanRename("CenterManager", "CenterManager");
        ReservedRoleGuard.EnsureCanRename("CustomOps", "RenamedOps");
    }

    [Fact]
    public void ReservedRoleGuard_blocks_deactivating_super_admin()
    {
        Assert.Throws<ForbiddenPermissionException>(() =>
            ReservedRoleGuard.EnsureCanDeactivate("SuperAdmin"));

        ReservedRoleGuard.EnsureCanDeactivate("ClinicDoctor");
    }

    [Fact]
    public async Task Sync_permissions_rejects_grant_beyond_actor_authorization()
    {
        var role = new Role { Id = "role-1", Name = "CustomOps", IsActive = true };
        var manager = CreateRoleManager(role);
        var permId = Guid.NewGuid();
        var excess = new Permission { Id = permId, Name = MilitaryHealthPermissions.Centers.Manage, IsActive = true };

        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(r => r.GetActiveByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Permission> { excess });

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePermission>());
        roles.Setup(r => r.GetRolePermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePermission>());

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.GetPermissions()).Returns(new List<string>
        {
            SystemPermissions.RoleManagement.AssignPermissions,
            SystemPermissions.UserManagement.Read,
        });

        var service = CreateRoleService(manager.Object, gate.Object, roles.Object, permissions.Object);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.SyncPermissionsForRoleAsync(new AssignPermissionsDto
            {
                RoleId = role.Id,
                PermissionsIds = new List<Guid> { permId }
            }));
    }

    [Fact]
    public async Task Update_role_rejects_renaming_reserved_role()
    {
        var role = new Role { Id = "role-sa", Name = "SuperAdmin", IsActive = true };
        var manager = CreateRoleManager(role);
        manager.Setup(m => m.FindByNameAsync("RenamedAdmin")).ReturnsAsync((Role?)null);
        manager.Setup(m => m.UpdateAsync(It.IsAny<Role>())).ReturnsAsync(IdentityResult.Success);

        var gate = new Mock<IPermissionGate>();
        var service = CreateRoleService(manager.Object, gate.Object, Mock.Of<IRoleRepository>(), Mock.Of<IPermissionRepository>());

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.UpdateRoleAsync(role.Id, new UpdateRoleDto { Name = "RenamedAdmin", Description = "x" }));
    }

    [Fact]
    public async Task Toggle_role_status_rejects_deactivating_super_admin()
    {
        var role = new Role { Id = Guid.NewGuid().ToString(), Name = "SuperAdmin", IsActive = true };
        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetByIdAsync(role.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(role);

        var gate = new Mock<IPermissionGate>();
        var service = CreateRoleService(
            CreateRoleManager(role).Object, gate.Object, roles.Object, Mock.Of<IPermissionRepository>());

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ToggleRoleStatusAsync(Guid.Parse(role.Id)));
    }

    [Fact]
    public async Task Delete_role_deactivates_non_reserved_role_and_invalidates_sessions()
    {
        var role = new Role { Id = "role-custom", Name = "TempRole", IsActive = true };
        var manager = CreateRoleManager(role);
        manager.Setup(m => m.UpdateAsync(role)).ReturnsAsync(IdentityResult.Success);
        manager.Setup(m => m.DeleteAsync(It.IsAny<Role>())).ThrowsAsync(new InvalidOperationException("physical delete"));

        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetUserIdsByRoleIdAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        users.Setup(r => r.GetActiveUserIdsExceptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRolePermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePermission>());

        var gate = new Mock<IPermissionGate>();
        var service = new RoleService(
            manager.Object, users.Object, null!, null!, new TestCurrentUser(), gate.Object,
            roles.Object, Mock.Of<IPermissionRepository>(), Mock.Of<IRefreshTokenRepository>(), Mock.Of<IUnitOfWork>());

        await service.DeleteRoleAsync(role.Id);

        Assert.False(role.IsActive);
        manager.Verify(m => m.DeleteAsync(It.IsAny<Role>()), Times.Never);
    }

    [Fact]
    public async Task Toggle_permission_for_role_rejects_reactivate_beyond_actor_authorization()
    {
        var roleId = "role-1";
        var permission = new Permission
        {
            Id = Guid.NewGuid(),
            Name = SystemPermissions.UserManagement.Delete,
            IsActive = true
        };
        var existing = new RolePermission
        {
            RoleId = roleId,
            PermissionId = permission.Id,
            IsActive = false,
            GrantedByUserId = "actor-1",
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedAt = DateTimeOffset.UtcNow.AddHours(-1),
            RevokedByUserId = "actor-1",
            RevokeReason = IamRetention.RolePermissionWithdrawn
        };

        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(r => r.FindByIdAsync(permission.Id, It.IsAny<CancellationToken>())).ReturnsAsync(permission);
        permissions.Setup(r => r.FindRolePermissionAsync(roleId, permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRolePermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePermission>());

        var roleStore = new Mock<IRoleStore<Role>>();
        var roleManager = new Mock<RoleManager<Role>>(
            roleStore.Object, Array.Empty<IRoleValidator<Role>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), Mock.Of<ILogger<RoleManager<Role>>>());
        roleManager.Setup(m => m.FindByIdAsync(roleId)).ReturnsAsync(new Role { Id = roleId, Name = "Custom", IsActive = true });

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.GetPermissions()).Returns(new List<string>
        {
            SystemPermissions.RoleManagement.AssignPermissions,
            SystemPermissions.UserManagement.Read,
        });

        var service = new PermissionService(
            permissions.Object, roles.Object, Mock.Of<IUserRepository>(), Mock.Of<IRefreshTokenRepository>(),
            Mock.Of<IUnitOfWork>(), roleManager.Object, null!, gate.Object, new TestCurrentUser(), null!);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.TogglePermissionForRoleAsync(roleId, permission.Id));
    }

    [Fact]
    public async Task EnsureAtLeastOneIamAdmin_blocks_stripping_last_admin_capabilities_from_role()
    {
        var roleId = "role-sa";
        var adminUser = "admin-1";
        var adminPerms = new[]
        {
            SystemPermissions.UserManagement.AssignRoles,
            SystemPermissions.RoleManagement.AssignPermissions,
            SystemPermissions.PermissionManagement.ChangeStatus,
        };

        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetActiveUserIdsExceptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { adminUser });
        users.Setup(r => r.GetDirectPermissionNamesAsync(adminUser, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        users.Setup(r => r.GetActiveRoleIdsAsync(adminUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { roleId });

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRolePermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminPerms.Select(name => new RolePermission
            {
                RoleId = roleId,
                IsActive = true,
                Permission = new Permission { Name = name, IsActive = true }
            }).ToList());

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            IamAdminGuard.EnsureAtLeastOneIamAdminRemainsAfterRolePermissionChangeAsync(
                users.Object, roles.Object, roleId, Array.Empty<string>(), adminPerms));
    }

    private static Mock<RoleManager<Role>> CreateRoleManager(Role role)
    {
        var store = new Mock<IRoleStore<Role>>();
        var manager = new Mock<RoleManager<Role>>(
            store.Object, Array.Empty<IRoleValidator<Role>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), Mock.Of<ILogger<RoleManager<Role>>>());
        manager.Setup(m => m.FindByIdAsync(role.Id)).ReturnsAsync(role);
        manager.Setup(m => m.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        return manager;
    }

    private static RoleService CreateRoleService(
        RoleManager<Role> roleManager,
        IPermissionGate gate,
        IRoleRepository roles,
        IPermissionRepository permissions)
    {
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetUserIdsByRoleIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        users.Setup(r => r.GetActiveUserIdsExceptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        return new RoleService(
            roleManager, users.Object, null!, null!, new TestCurrentUser(), gate,
            roles, permissions, Mock.Of<IRefreshTokenRepository>(), Mock.Of<IUnitOfWork>());
    }
}
