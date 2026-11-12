using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Entities.Auth;
using Domain.Enums;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Application.Tests;

/// <summary>
/// MH-IAM-SEC-02: seed must not reverse admin IAM decisions; role sync parks catalog-inactive links;
/// seed audit is marked as SYSTEM_SEED without a fake client IP.
/// </summary>
public class IamSec02SeedSyncAuditTests
{
    #region A — DbInitializer / IamSeedCatalogOps

    [Fact]
    public async Task Seed_ensure_role_leaves_admin_deactivated_role_inactive()
    {
        await using var db = CreateDb();
        db.Roles.Add(new Role
        {
            Id = "role-reception",
            Name = "Reception",
            NormalizedName = "RECEPTION",
            IsActive = false,
            Scope = RoleScope.Center,
            Description = "قديم"
        });
        await db.SaveChangesAsync();

        var result = await IamSeedCatalogOps.EnsureRoleAsync(
            db, "Reception", "RECEPTION", "الاستقبال", RoleScope.Center);

        Assert.False(result.IsActive);
        Assert.Equal("الاستقبال", result.Description);
        Assert.False((await db.Roles.SingleAsync(r => r.Id == "role-reception")).IsActive);
    }

    [Fact]
    public async Task Seed_ensure_role_creates_missing_seed_role()
    {
        await using var db = CreateDb();

        var created = await IamSeedCatalogOps.EnsureRoleAsync(
            db, "Doctor", "DOCTOR", "طبيب", RoleScope.Center);

        Assert.True(created.IsActive);
        Assert.Equal("Doctor", created.Name);
        Assert.Equal(1, await db.Roles.CountAsync());
    }

    [Fact]
    public async Task Seed_ensure_role_repeated_does_not_change_existing_active_or_inactive_status()
    {
        await using var db = CreateDb();
        db.Roles.Add(new Role
        {
            Id = "role-sa",
            Name = "SuperAdmin",
            NormalizedName = "SUPERADMIN",
            IsActive = false,
            Scope = RoleScope.Central
        });
        await db.SaveChangesAsync();

        await IamSeedCatalogOps.EnsureRoleAsync(db, "SuperAdmin", "SUPERADMIN", null, RoleScope.Central);
        await IamSeedCatalogOps.EnsureRoleAsync(db, "SuperAdmin", "SUPERADMIN", null, RoleScope.Central);

        Assert.False((await db.Roles.SingleAsync()).IsActive);
        Assert.Equal(1, await db.Roles.CountAsync());
    }

    [Fact]
    public async Task Deactivated_seed_role_does_not_grant_effective_permissions()
    {
        await using var db = CreateDb();
        var userId = "user-1";
        var roleId = "role-1";
        var permission = new Permission { Id = Guid.NewGuid(), Name = "Members.View", IsActive = true };
        db.Permissions.Add(permission);
        db.Roles.Add(new Role { Id = roleId, Name = "Reception", NormalizedName = "RECEPTION", IsActive = false });
        db.Users.Add(new User { Id = userId, UserName = "u1", NormalizedUserName = "U1", IsActive = true });
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = roleId,
            AssignedByUserId = "actor-1",
            AssignedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        db.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = permission.Id,
            Permission = permission,
            GrantedByUserId = "actor-1",
            GrantedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();

        Assert.Empty(await new PermissionRepository(db).GetPermissionNamesViaRolesAsync(userId));
        Assert.False((await IamSeedCatalogOps.EnsureRoleAsync(db, "Reception", "RECEPTION")).IsActive);
        Assert.Empty(await new PermissionRepository(db).GetPermissionNamesViaRolesAsync(userId));
    }

    [Fact]
    public async Task Seed_ensure_permission_leaves_admin_deactivated_catalog_inactive()
    {
        await using var db = CreateDb();
        var deactivated = new Permission
        {
            Id = Guid.NewGuid(),
            Name = "Members.View",
            Description = "عرض",
            IsActive = false
        };
        db.Permissions.Add(deactivated);
        await db.SaveChangesAsync();

        var result = await IamSeedCatalogOps.EnsurePermissionAsync(db, "Members.View", "عرض محدّث");

        Assert.Equal(deactivated.Id, result.Id);
        Assert.False(result.IsActive);
        Assert.False((await db.Permissions.SingleAsync(p => p.Id == deactivated.Id)).IsActive);
    }

    [Fact]
    public async Task Seed_ensure_role_permission_leaves_admin_revoked_link_revoked()
    {
        await using var db = CreateDb();
        var roleId = "role-1";
        var permission = new Permission { Id = Guid.NewGuid(), Name = "Members.View", IsActive = true };
        db.Permissions.Add(permission);
        var revoked = new RolePermission
        {
            RoleId = roleId,
            PermissionId = permission.Id,
            GrantedByUserId = "admin-1",
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-2),
            IsActive = false,
            RevokedAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedByUserId = "admin-1",
            RevokeReason = IamRetention.RolePermissionWithdrawn
        };
        db.RolePermissions.Add(revoked);
        await db.SaveChangesAsync();

        var changed = await IamSeedCatalogOps.EnsurePermissionsAssignedToRoleAsync(
            db, roleId, "seed-actor", new[] { "Members.View" });

        Assert.False(changed);
        var link = await db.RolePermissions.SingleAsync(rp => rp.RoleId == roleId && rp.PermissionId == permission.Id);
        Assert.False(link.IsActive);
        Assert.Equal(IamRetention.RolePermissionWithdrawn, link.RevokeReason);
        Assert.Equal("admin-1", link.RevokedByUserId);
        Assert.NotNull(link.RevokedAt);
    }

    [Fact]
    public async Task Seed_ensure_permission_creates_missing_catalog_row()
    {
        await using var db = CreateDb();

        var created = await IamSeedCatalogOps.EnsurePermissionAsync(db, "Centers.Manage", "إدارة المراكز");

        Assert.True(created.IsActive);
        Assert.Equal("Centers.Manage", created.Name);
        Assert.Equal(1, await db.Permissions.CountAsync());
    }

    [Fact]
    public async Task Seed_ensure_role_permission_creates_missing_matrix_grant()
    {
        await using var db = CreateDb();
        var roleId = "role-reception";
        var permission = new Permission { Id = Guid.NewGuid(), Name = "Members.View", IsActive = true };
        db.Permissions.Add(permission);
        await db.SaveChangesAsync();

        var changed = await IamSeedCatalogOps.EnsurePermissionsAssignedToRoleAsync(
            db, roleId, "seed-actor", new[] { "Members.View" });

        Assert.True(changed);
        var link = await db.RolePermissions.SingleAsync();
        Assert.True(link.IsActive);
        Assert.Equal(roleId, link.RoleId);
        Assert.Equal(permission.Id, link.PermissionId);
        Assert.Null(link.RevokedAt);
    }

    [Fact]
    public async Task Seed_ensure_role_permission_is_idempotent_and_does_not_duplicate()
    {
        await using var db = CreateDb();
        var roleId = "role-1";
        var permission = new Permission { Id = Guid.NewGuid(), Name = "Members.View", IsActive = true };
        db.Permissions.Add(permission);
        await db.SaveChangesAsync();

        Assert.True(await IamSeedCatalogOps.EnsurePermissionsAssignedToRoleAsync(
            db, roleId, "seed-actor", new[] { "Members.View" }));
        Assert.False(await IamSeedCatalogOps.EnsurePermissionsAssignedToRoleAsync(
            db, roleId, "seed-actor", new[] { "Members.View" }));

        Assert.Equal(1, await db.RolePermissions.CountAsync());
    }

    [Fact]
    public async Task Seed_migrate_permission_name_still_moves_active_role_link()
    {
        await using var db = CreateDb();
        var roleId = "role-1";
        var oldPerm = new Permission { Id = Guid.NewGuid(), Name = "ReportsAuditLogs.Export", IsActive = true };
        var newPerm = new Permission { Id = Guid.NewGuid(), Name = "AuditLog.Export", IsActive = true };
        db.Permissions.AddRange(oldPerm, newPerm);
        db.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = oldPerm.Id,
            GrantedByUserId = "admin-1",
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-1),
            IsActive = true
        });
        await db.SaveChangesAsync();

        await IamSeedCatalogOps.MigratePermissionNameAsync(
            db, "ReportsAuditLogs.Export", "AuditLog.Export", "تصدير", "migrator-1");

        Assert.False((await db.Permissions.SingleAsync(p => p.Id == oldPerm.Id)).IsActive);
        var link = await db.RolePermissions.SingleAsync(rp => rp.IsActive);
        Assert.Equal(newPerm.Id, link.PermissionId);
    }

    [Fact]
    public async Task Seed_ensure_role_permission_does_not_invalidate_when_revoked_row_is_left_alone()
    {
        await using var db = CreateDb();
        var roleId = "role-1";
        var permission = new Permission { Id = Guid.NewGuid(), Name = "Members.View", IsActive = true };
        db.Permissions.Add(permission);
        db.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = permission.Id,
            GrantedByUserId = "admin-1",
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-2),
            IsActive = false,
            RevokedAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedByUserId = "admin-1",
            RevokeReason = IamRetention.RolePermissionWithdrawn
        });
        await db.SaveChangesAsync();

        var changed = await IamSeedCatalogOps.EnsurePermissionsAssignedToRoleAsync(
            db, roleId, "seed-actor", new[] { "Members.View" });

        Assert.False(changed);
    }

    #endregion

    #region B — Role sync parks catalog-inactive links

    [Fact]
    public async Task Sync_preserves_active_role_permission_when_catalog_permission_is_inactive()
    {
        var role = new Role { Id = "role-1", Name = "CustomOps", IsActive = true, PermissionSetVersion = 1 };
        var activeId = Guid.NewGuid();
        var parkedId = Guid.NewGuid();
        var active = new Permission { Id = activeId, Name = SystemPermissions.UserManagement.Read, IsActive = true };
        var parked = new Permission { Id = parkedId, Name = SystemPermissions.UserManagement.Edit, IsActive = false };

        var existingLinks = new List<RolePermission>
        {
            ActiveLink(role.Id, active),
            ActiveLink(role.Id, parked),
        };

        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(r => r.GetActiveByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) =>
                ids.Contains(activeId) ? new List<Permission> { active } : new List<Permission>());
        permissions.Setup(r => r.GetActiveIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) =>
                ids.Where(id => id == activeId).ToList());

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLinks);
        roles.Setup(r => r.GetRolePermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLinks.Where(l => l.PermissionId == activeId).ToList());

        var gate = ActorGate(SystemPermissions.UserManagement.Read, SystemPermissions.UserManagement.Edit);
        var uow = CreateUnitOfWorkMock();
        var service = CreateRoleService(CreateRoleManager(role).Object, gate.Object, roles.Object, permissions.Object, uow.Object);

        var result = await service.SyncPermissionsForRoleAsync(new AssignPermissionsDto
        {
            RoleId = role.Id,
            PermissionsIds = new List<Guid> { activeId },
            PermissionSetVersion = 1
        });

        Assert.Equal(0, result.Removed);
        Assert.Equal(0, result.Added);
        Assert.Equal(1, role.PermissionSetVersion);
        Assert.True(existingLinks.Single(l => l.PermissionId == parkedId).IsActive);
        Assert.Null(existingLinks.Single(l => l.PermissionId == parkedId).RevokedAt);
    }

    [Fact]
    public async Task Sync_still_withdraws_catalog_active_permission_omitted_from_final_set()
    {
        var role = new Role { Id = "role-1", Name = "CustomOps", IsActive = true, PermissionSetVersion = 2 };
        var keepId = Guid.NewGuid();
        var dropId = Guid.NewGuid();
        var keep = new Permission { Id = keepId, Name = SystemPermissions.UserManagement.Read, IsActive = true };
        var drop = new Permission { Id = dropId, Name = SystemPermissions.UserManagement.Edit, IsActive = true };

        var existingLinks = new List<RolePermission>
        {
            ActiveLink(role.Id, keep),
            ActiveLink(role.Id, drop),
        };

        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(r => r.GetActiveByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Permission> { keep });
        permissions.Setup(r => r.GetActiveIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { keepId, dropId });

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLinks);
        roles.Setup(r => r.GetRolePermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLinks);

        var gate = ActorGate(SystemPermissions.UserManagement.Read, SystemPermissions.UserManagement.Edit);
        var uow = CreateUnitOfWorkMock();
        var service = CreateRoleService(CreateRoleManager(role).Object, gate.Object, roles.Object, permissions.Object, uow.Object);

        var result = await service.SyncPermissionsForRoleAsync(new AssignPermissionsDto
        {
            RoleId = role.Id,
            PermissionsIds = new List<Guid> { keepId },
            PermissionSetVersion = 2
        });

        Assert.Equal(1, result.Removed);
        Assert.False(existingLinks.Single(l => l.PermissionId == dropId).IsActive);
        Assert.Equal(IamRetention.RolePermissionWithdrawn, existingLinks.Single(l => l.PermissionId == dropId).RevokeReason);
        Assert.Equal(3, role.PermissionSetVersion);
    }

    [Fact]
    public async Task Catalog_inactive_permission_is_excluded_from_effective_role_permission_names()
    {
        await using var db = CreateDb();
        var userId = "user-1";
        var roleId = "role-1";
        var active = new Permission { Id = Guid.NewGuid(), Name = "Members.View", IsActive = true };
        var inactive = new Permission { Id = Guid.NewGuid(), Name = "Members.Create", IsActive = false };
        db.Permissions.AddRange(active, inactive);
        db.Roles.Add(new Role { Id = roleId, Name = "Reception", NormalizedName = "RECEPTION", IsActive = true });
        db.Users.Add(new User { Id = userId, UserName = "u1", NormalizedUserName = "U1", IsActive = true });
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = roleId,
            AssignedByUserId = "actor-1",
            AssignedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        db.RolePermissions.AddRange(
            new RolePermission
            {
                RoleId = roleId,
                PermissionId = active.Id,
                Permission = active,
                GrantedByUserId = "actor-1",
                GrantedAt = DateTimeOffset.UtcNow,
                IsActive = true
            },
            new RolePermission
            {
                RoleId = roleId,
                PermissionId = inactive.Id,
                Permission = inactive,
                GrantedByUserId = "actor-1",
                GrantedAt = DateTimeOffset.UtcNow,
                IsActive = true
            });
        await db.SaveChangesAsync();

        var names = await new PermissionRepository(db).GetPermissionNamesViaRolesAsync(userId);

        Assert.Contains("Members.View", names);
        Assert.DoesNotContain("Members.Create", names);
    }

    [Fact]
    public async Task Catalog_reactivation_does_not_revive_admin_revoked_role_permission()
    {
        await using var db = CreateDb();
        var permission = new Permission { Id = Guid.NewGuid(), Name = "Members.View", IsActive = false };
        db.Permissions.Add(permission);
        db.RolePermissions.Add(new RolePermission
        {
            RoleId = "role-1",
            PermissionId = permission.Id,
            GrantedByUserId = "admin-1",
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-3),
            IsActive = false,
            RevokedAt = DateTimeOffset.UtcNow.AddDays(-2),
            RevokedByUserId = "admin-1",
            RevokeReason = IamRetention.RolePermissionWithdrawn
        });
        await db.SaveChangesAsync();

        permission.IsActive = true;
        await db.SaveChangesAsync();

        var link = await db.RolePermissions.SingleAsync();
        Assert.False(link.IsActive);
        Assert.Equal(IamRetention.RolePermissionWithdrawn, link.RevokeReason);
    }

    [Fact]
    public async Task Catalog_reactivation_restores_effective_access_for_preserved_active_role_permission()
    {
        await using var db = CreateDb();
        var userId = "user-1";
        var roleId = "role-1";
        var permission = new Permission { Id = Guid.NewGuid(), Name = "Members.View", IsActive = false };
        db.Permissions.Add(permission);
        db.Roles.Add(new Role { Id = roleId, Name = "Reception", NormalizedName = "RECEPTION", IsActive = true });
        db.Users.Add(new User { Id = userId, UserName = "u1", NormalizedUserName = "U1", IsActive = true });
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = roleId,
            AssignedByUserId = "actor-1",
            AssignedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        db.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = permission.Id,
            Permission = permission,
            GrantedByUserId = "actor-1",
            GrantedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();

        Assert.Empty(await new PermissionRepository(db).GetPermissionNamesViaRolesAsync(userId));

        permission.IsActive = true;
        await db.SaveChangesAsync();

        var names = await new PermissionRepository(db).GetPermissionNamesViaRolesAsync(userId);
        Assert.Contains("Members.View", names);
    }

    [Fact]
    public async Task Sync_rejects_grant_beyond_actor_authorization_unchanged()
    {
        var role = new Role { Id = "role-1", Name = "CustomOps", IsActive = true, PermissionSetVersion = 1 };
        var readId = Guid.NewGuid();
        var excessId = Guid.NewGuid();
        var read = new Permission { Id = readId, Name = SystemPermissions.UserManagement.Read, IsActive = true };
        var excess = new Permission { Id = excessId, Name = MilitaryHealthPermissions.Centers.Manage, IsActive = true };

        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(r => r.GetActiveByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Permission> { read, excess });
        permissions.Setup(r => r.GetActiveIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePermission>());
        roles.Setup(r => r.GetRolePermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePermission>());

        var gate = ActorGate(SystemPermissions.UserManagement.Read);
        var service = CreateRoleService(CreateRoleManager(role).Object, gate.Object, roles.Object, permissions.Object);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.SyncPermissionsForRoleAsync(new AssignPermissionsDto
            {
                RoleId = role.Id,
                PermissionsIds = new List<Guid> { readId, excessId },
                PermissionSetVersion = 1
            }));
    }

    [Fact]
    public async Task Sync_throws_when_GetActiveIdsAsync_returns_null_instead_of_fallback_withdraw()
    {
        var role = new Role { Id = "role-1", Name = "CustomOps", IsActive = true, PermissionSetVersion = 1 };
        var activeId = Guid.NewGuid();
        var parkedId = Guid.NewGuid();
        var active = new Permission { Id = activeId, Name = SystemPermissions.UserManagement.Read, IsActive = true };
        var parked = new Permission { Id = parkedId, Name = SystemPermissions.UserManagement.Edit, IsActive = false };
        var existingLinks = new List<RolePermission>
        {
            ActiveLink(role.Id, active),
            ActiveLink(role.Id, parked),
        };

        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(r => r.GetActiveByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Permission> { active });
        permissions.Setup(r => r.GetActiveIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>)null!);

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLinks);

        var gate = ActorGate(SystemPermissions.UserManagement.Read);
        var service = CreateRoleService(CreateRoleManager(role).Object, gate.Object, roles.Object, permissions.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SyncPermissionsForRoleAsync(new AssignPermissionsDto
            {
                RoleId = role.Id,
                PermissionsIds = new List<Guid> { activeId },
                PermissionSetVersion = 1
            }));

        Assert.Contains("GetActiveIdsAsync", ex.Message);
        Assert.True(existingLinks.Single(l => l.PermissionId == parkedId).IsActive);
        Assert.Null(existingLinks.Single(l => l.PermissionId == parkedId).RevokedAt);
    }

    #endregion

    #region C — Seed audit context / reactivation stamp / migrate

    [Fact]
    public async Task Seed_audit_context_uses_system_identity_without_loopback_ip()
    {
        var currentUser = new TestCurrentUser();
        currentUser.SetUser("actor-http", "HttpUser", "10.0.0.8");
        currentUser.SetRequest("Mozilla/5.0", Guid.NewGuid());

        IamSeedCatalogOps.ApplySystemSeedAuditContext(currentUser);

        Assert.Equal(IamSeedCatalogOps.SystemSeedUserId, currentUser.UserId);
        Assert.Equal(IamSeedCatalogOps.SystemSeedUserName, currentUser.UserName);
        Assert.Equal(string.Empty, currentUser.IPAddress);
        Assert.Equal(IamSeedCatalogOps.SystemSeedUserAgent, currentUser.UserAgent);

        await using var db = CreateDb(currentUser);
        db.Permissions.Add(new Permission
        {
            Id = Guid.NewGuid(),
            Name = "Seed.Probe",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var audit = await db.AuditEvents.SingleAsync();
        Assert.Null(audit.ActorUserId);
        Assert.Null(audit.IPAddress);
        Assert.Equal(IamSeedCatalogOps.SystemSeedUserAgent, audit.UserAgent);
        Assert.NotEqual("::1", audit.IPAddress?.ToString());
    }

    [Fact]
    public async Task Http_request_audit_still_records_actor_and_client_ip()
    {
        var currentUser = new TestCurrentUser();
        currentUser.SetUser("actor-42", "Operator", "203.0.113.10");
        currentUser.SetRequest("unit-test-browser", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        await using var db = CreateDb(currentUser);
        db.Permissions.Add(new Permission
        {
            Id = Guid.NewGuid(),
            Name = "Http.Probe",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var audit = await db.AuditEvents.SingleAsync();
        Assert.Equal("actor-42", audit.ActorUserId);
        Assert.Equal("203.0.113.10", audit.IPAddress!.ToString());
        Assert.Equal("unit-test-browser", audit.UserAgent);
        Assert.NotEqual(IamSeedCatalogOps.SystemSeedUserAgent, audit.UserAgent);
    }

    [Fact]
    public void Current_user_default_ip_is_empty_not_loopback()
    {
        var service = new CurrentUserService();
        Assert.Equal(string.Empty, service.IPAddress);
        Assert.NotEqual("::1", service.IPAddress);
    }

    [Fact]
    public async Task Toggle_permission_status_reactivation_invalidates_users_with_permission()
    {
        var permission = new Permission { Id = Guid.NewGuid(), Name = "Members.View", IsActive = false };
        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(r => r.FindByIdAsync(permission.Id, It.IsAny<CancellationToken>())).ReturnsAsync(permission);
        permissions.Setup(r => r.GetRoleIdsByPermissionIdAsync(permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "role-1" });
        permissions.Setup(r => r.GetUserIdsByPermissionIdAsync(permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var user = new User { Id = "user-1", UserName = "u1", IsActive = true, SecurityStamp = "stamp-old" };
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetUserIdsByRoleIdAsync("role-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { user.Id });
        users.Setup(r => r.FindByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var tokens = new Mock<IRefreshTokenRepository>();
        tokens.Setup(r => r.GetActiveByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserRefreshToken>());

        var userManager = new Mock<UserManager<User>>(
            Mock.Of<IUserStore<User>>(),
            Mock.Of<IOptions<IdentityOptions>>(),
            Mock.Of<IPasswordHasher<User>>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<User>>>());
        userManager.Setup(m => m.UpdateSecurityStampAsync(user)).ReturnsAsync(IdentityResult.Success);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var gate = new Mock<IPermissionGate>();
        var service = new PermissionService(
            permissions.Object,
            Mock.Of<IRoleRepository>(),
            users.Object,
            tokens.Object,
            uow.Object,
            null!,
            userManager.Object,
            gate.Object,
            new TestCurrentUser(),
            null!);

        await service.TogglePermissionStatusAsync(permission.Id);

        Assert.True(permission.IsActive);
        userManager.Verify(m => m.UpdateSecurityStampAsync(user), Times.Once);
    }

    [Fact]
    public async Task Migrate_permission_name_reactivates_revoked_counterpart_only_when_old_link_is_active()
    {
        await using var db = CreateDb();
        var roleId = "role-1";
        var oldPerm = new Permission { Id = Guid.NewGuid(), Name = "Old.Export", IsActive = true };
        var newPerm = new Permission { Id = Guid.NewGuid(), Name = "New.Export", IsActive = true };
        db.Permissions.AddRange(oldPerm, newPerm);
        db.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = oldPerm.Id,
            GrantedByUserId = "admin-1",
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-3),
            IsActive = true
        });
        db.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = newPerm.Id,
            GrantedByUserId = "admin-1",
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-2),
            IsActive = false,
            RevokedAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedByUserId = "admin-1",
            RevokeReason = IamRetention.RolePermissionWithdrawn
        });
        await db.SaveChangesAsync();

        await IamSeedCatalogOps.MigratePermissionNameAsync(db, "Old.Export", "New.Export", "desc", "migrator-1");

        var newLink = await db.RolePermissions.SingleAsync(rp => rp.PermissionId == newPerm.Id);
        Assert.True(newLink.IsActive);
        Assert.Null(newLink.RevokedAt);
        var oldLink = await db.RolePermissions.SingleAsync(rp => rp.PermissionId == oldPerm.Id);
        Assert.False(oldLink.IsActive);
        Assert.Equal(IamRetention.PermissionRenamed, oldLink.RevokeReason);
        Assert.False((await db.Permissions.SingleAsync(p => p.Id == oldPerm.Id)).IsActive);
    }

    [Fact]
    public async Task Migrate_permission_name_does_not_revive_when_old_link_was_already_withdrawn()
    {
        await using var db = CreateDb();
        var roleId = "role-1";
        var oldPerm = new Permission { Id = Guid.NewGuid(), Name = "Old.Export", IsActive = true };
        var newPerm = new Permission { Id = Guid.NewGuid(), Name = "New.Export", IsActive = true };
        db.Permissions.AddRange(oldPerm, newPerm);
        db.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = oldPerm.Id,
            GrantedByUserId = "admin-1",
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-3),
            IsActive = false,
            RevokedAt = DateTimeOffset.UtcNow.AddDays(-2),
            RevokedByUserId = "admin-1",
            RevokeReason = IamRetention.RolePermissionWithdrawn
        });
        db.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = newPerm.Id,
            GrantedByUserId = "admin-1",
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-3),
            IsActive = false,
            RevokedAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedByUserId = "admin-1",
            RevokeReason = IamRetention.RolePermissionWithdrawn
        });
        await db.SaveChangesAsync();

        await IamSeedCatalogOps.MigratePermissionNameAsync(db, "Old.Export", "New.Export", "desc", "migrator-1");

        var newLink = await db.RolePermissions.SingleAsync(rp => rp.PermissionId == newPerm.Id);
        Assert.False(newLink.IsActive);
        Assert.Equal(IamRetention.RolePermissionWithdrawn, newLink.RevokeReason);
    }

    #endregion

    #region Helpers

    private static AppDbContext CreateDb(ICurrentUserService? currentUser = null)
    {
        var user = currentUser ?? new TestCurrentUser();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, user, new TestCenter(null, true), new AuditInterceptor(user));
    }

    private static Mock<RoleManager<Role>> CreateRoleManager(Role role)
    {
        var store = new Mock<IRoleStore<Role>>();
        var manager = new Mock<RoleManager<Role>>(
            store.Object, Array.Empty<IRoleValidator<Role>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), Mock.Of<ILogger<RoleManager<Role>>>());
        manager.Setup(m => m.FindByIdAsync(role.Id)).ReturnsAsync(role);
        return manager;
    }

    private static RoleService CreateRoleService(
        RoleManager<Role> roleManager,
        IPermissionGate gate,
        IRoleRepository roles,
        IPermissionRepository permissions,
        IUnitOfWork? unitOfWork = null)
    {
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetUserIdsByRoleIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        users.Setup(r => r.GetActiveUserIdsExceptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var actor = new TestCurrentUser();
        actor.SetUser("actor-1", "admin", "127.0.0.1");

        return new RoleService(
            roleManager, users.Object, null!, null!, actor, gate,
            roles, permissions, Mock.Of<IRefreshTokenRepository>(), unitOfWork ?? Mock.Of<IUnitOfWork>());
    }

    private static Mock<IUnitOfWork> CreateUnitOfWorkMock()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoopTransaction());
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return uow;
    }

    private static Mock<IPermissionGate> ActorGate(params string[] permissions)
    {
        var gate = new Mock<IPermissionGate>();
        var list = new List<string> { SystemPermissions.RoleManagement.AssignPermissions };
        list.AddRange(permissions);
        gate.Setup(g => g.GetPermissions()).Returns(list);
        return gate;
    }

    private static RolePermission ActiveLink(string roleId, Permission permission) =>
        new()
        {
            RoleId = roleId,
            PermissionId = permission.Id,
            Permission = permission,
            IsActive = true,
            GrantedByUserId = "seed",
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

    private sealed class NoopTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public void Commit() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    #endregion
}
