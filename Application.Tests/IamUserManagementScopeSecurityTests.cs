using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Application.Tests;

/// <summary>
/// MH-IAM-SEC-01 — انحدار نطاق إدارة المستخدمين وحماية سحب Toggle.
/// يستخدم InMemory فقط؛ لا يتصل بقاعدة التطوير.
/// </summary>
public class IamUserManagementScopeSecurityTests
{
    private static readonly Guid Idlib = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Damascus = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Central_read_does_not_expand_local_assign_permissions_to_foreign_center()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedCenterAsync(db, Damascus, "DAM");
        await SeedUserAsync(db, "actor-1", "idlib.mgr");
        await SeedUserAsync(db, "target-dam", "dam.user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-dam", Damascus);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);
        await SeedDirectPermissionAsync(db, "actor-1", SystemPermissions.UserManagement.Read, centerId: null);
        await SeedIamKeeperAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            SystemPermissions.UserManagement.Read,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, activeCenter: Idlib, central: false, gate);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
            {
                UserId = "target-dam",
                PermissionId = [viewId],
                CenterId = Damascus
            }));

        Assert.Equal(0, await db.UserPermissions.CountAsync(p => p.UserId == "target-dam" && p.IsActive));
        harness.UserManager.Verify(m => m.UpdateSecurityStampAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Local_assign_with_central_read_cannot_grant_central_scope_via_null_center()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedUserAsync(db, "actor-1", "idlib.mgr");
        await SeedUserAsync(db, "target-1", "idlib.user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-1", Idlib);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);
        await SeedDirectPermissionAsync(db, "actor-1", SystemPermissions.UserManagement.Read, centerId: null);
        await SeedIamKeeperAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            SystemPermissions.UserManagement.Read,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, activeCenter: Idlib, central: false, gate);

        await harness.Service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
        {
            UserId = "target-1",
            PermissionId = [viewId],
            CenterId = null
        });

        var row = await db.UserPermissions.SingleAsync(p => p.UserId == "target-1" && p.IsActive);
        Assert.Equal(Idlib, row.CenterId);
        Assert.Null(await db.UserPermissions.FirstOrDefaultAsync(p =>
            p.UserId == "target-1" && p.IsActive && p.CenterId == null));
    }

    [Fact]
    public async Task Local_assign_succeeds_for_same_center_target()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedUserAsync(db, "actor-1", "idlib.mgr");
        await SeedUserAsync(db, "target-1", "idlib.user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-1", Idlib);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);
        await SeedDirectPermissionAsync(db, "actor-1", SystemPermissions.UserManagement.Read, centerId: null);
        await SeedIamKeeperAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            SystemPermissions.UserManagement.Read,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, activeCenter: Idlib, central: false, gate);

        await harness.Service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
        {
            UserId = "target-1",
            PermissionId = [viewId],
            CenterId = Idlib
        });

        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == viewId && p.CenterId == Idlib && p.IsActive));
        harness.UserManager.Verify(m => m.UpdateSecurityStampAsync(It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task Central_assign_permissions_retains_central_grant_capability()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedUserAsync(db, "actor-1", "central.admin");
        await SeedUserAsync(db, "target-1", "user");
        await SeedUserCenterAsync(db, "target-1", Idlib);
        await SeedIamKeeperAsync(db);
        await SeedRoleLinkAsync(db, "actor-1", "SuperAdmin", null);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, activeCenter: null, central: true, gate);

        await harness.Service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
        {
            UserId = "target-1",
            PermissionId = [viewId],
            CenterId = null
        });

        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == viewId && p.CenterId == null && p.IsActive));
    }

    [Fact]
    public async Task Unrelated_central_role_without_assign_permissions_does_not_unlock_write_scope()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedCenterAsync(db, Damascus, "DAM");
        await SeedUserAsync(db, "actor-1", "auditor.local");
        await SeedUserAsync(db, "target-dam", "dam.user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-dam", Damascus);
        await SeedRoleLinkAsync(db, "actor-1", "Auditor", null);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);
        await SeedIamKeeperAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, activeCenter: Idlib, central: false, gate);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
            {
                UserId = "target-dam",
                PermissionId = [viewId],
                CenterId = Damascus
            }));
    }

    [Fact]
    public async Task Toggle_revokes_permission_within_actor_delegation()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedUserAsync(db, "actor-1", "mgr");
        await SeedUserAsync(db, "target-1", "user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-1", Idlib);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);
        await SeedIamKeeperAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        await GrantDirectAsync(db, "target-1", viewId, Idlib);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, activeCenter: Idlib, central: false, gate);

        await harness.Service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
        {
            UserId = "target-1",
            PermissionId = [viewId],
            CenterId = Idlib
        });

        var row = await db.UserPermissions.SingleAsync(p => p.UserId == "target-1" && p.PermissionId == viewId);
        Assert.False(row.IsActive);
        Assert.Equal(IamRetention.DirectPermissionWithdrawn, row.RevokeReason);
    }

    [Fact]
    public async Task Toggle_rejects_revoke_outside_actor_delegation_without_partial_writes()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedUserAsync(db, "actor-1", "mgr");
        await SeedUserAsync(db, "target-1", "user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-1", Idlib);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);
        await SeedIamKeeperAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        var elevatedId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Centers.Manage);
        await GrantDirectAsync(db, "target-1", elevatedId, Idlib);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, activeCenter: Idlib, central: false, gate);

        var error = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
            {
                UserId = "target-1",
                PermissionId = [elevatedId],
                CenterId = Idlib
            }));

        Assert.Contains("سحب", error.Message);
        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == elevatedId && p.IsActive));
        harness.UserManager.Verify(m => m.UpdateSecurityStampAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Toggle_mixed_allowed_add_and_forbidden_revoke_rejects_entire_request()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedUserAsync(db, "actor-1", "mgr");
        await SeedUserAsync(db, "target-1", "user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-1", Idlib);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);
        await SeedIamKeeperAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        var elevatedId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Centers.Manage);
        await GrantDirectAsync(db, "target-1", elevatedId, Idlib);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, activeCenter: Idlib, central: false, gate);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
            {
                UserId = "target-1",
                PermissionId = [viewId, elevatedId],
                CenterId = Idlib
            }));

        Assert.False(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == viewId && p.IsActive));
        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == elevatedId && p.IsActive));
        harness.UserManager.Verify(m => m.UpdateSecurityStampAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Toggle_rejects_revoke_for_target_outside_shared_center()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedCenterAsync(db, Damascus, "DAM");
        await SeedUserAsync(db, "actor-1", "mgr");
        await SeedUserAsync(db, "target-dam", "dam.user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-dam", Damascus);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);
        await SeedIamKeeperAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        await GrantDirectAsync(db, "target-dam", viewId, Damascus);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, activeCenter: Idlib, central: false, gate);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
            {
                UserId = "target-dam",
                PermissionId = [viewId],
                CenterId = Damascus
            }));

        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-dam" && p.PermissionId == viewId && p.IsActive));
    }

    [Fact]
    public async Task Sync_direct_permissions_still_keeps_out_of_delegation_grants_silently()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedUserAsync(db, "actor-1", "mgr");
        await SeedUserAsync(db, "target-1", "user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-1", Idlib);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);
        await SeedIamKeeperAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        var elevatedId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Centers.Manage);
        await GrantDirectAsync(db, "target-1", viewId, Idlib);
        await GrantDirectAsync(db, "target-1", elevatedId, Idlib);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, activeCenter: Idlib, central: false, gate);

        var result = await harness.Service.SyncDirectPermissionsAsync(new SyncDirectPermissionsDto
        {
            UserId = "target-1",
            PermissionIds = [],
            CenterId = Idlib
        });

        Assert.Equal(1, result.Removed);
        Assert.True(result.SessionsInvalidated);
        var skipped = Assert.Single(result.SkippedRevokes);
        Assert.Equal(elevatedId, skipped.PermissionId);
        Assert.Equal(MilitaryHealthPermissions.Centers.Manage, skipped.Name);
        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == elevatedId && p.IsActive));
        Assert.False(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == viewId && p.IsActive));
    }

    [Fact]
    public void HasCentralAuthority_is_not_sufficient_for_central_operation_permission()
    {
        var roles = new[]
        {
            new CenterPermissionEvaluator.RoleAssignment("r1", "Auditor", null, true, true)
        };
        var directs = new[]
        {
            new CenterPermissionEvaluator.DirectAssignment(
                SystemPermissions.UserManagement.Read, null, true, true)
        };
        var links = Array.Empty<CenterPermissionEvaluator.RolePermissionLink>();

        Assert.True(CenterPermissionEvaluator.HasCentralAuthority(roles, directs));
        Assert.False(CenterPermissionEvaluator.HasCentralOperationPermission(
            roles, directs, links, SystemPermissions.UserManagement.AssignPermissions));
        Assert.True(CenterPermissionEvaluator.HasCentralOperationPermission(
            roles, directs, links, SystemPermissions.UserManagement.Read));
    }

    #region harness

    private static Mock<IPermissionGate> Gate(params string[] permissions)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.GetPermissions()).Returns(permissions);
        gate.Setup(g => g.EnsureAll(It.IsAny<string[]>()));
        return gate;
    }

    private static UserHarness CreateUserService(
        AppDbContext db,
        Guid? activeCenter,
        bool central,
        Mock<IPermissionGate> gate)
    {
        var manager = CreateUserManager();
        manager.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .Returns((string id) => db.Users.FirstOrDefaultAsync(u => u.Id == id));
        manager.Setup(m => m.UpdateAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);
        manager.Setup(m => m.UpdateSecurityStampAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);

        var mapper = new Mock<IMapper>();
        var sanitizer = new Mock<ITextInputSanitizer>();
        sanitizer.Setup(s => s.SanitizePlainText(It.IsAny<string?>())).Returns((string? value) => value);

        var actor = new TestCurrentUser();
        actor.SetUser("actor-1", "manager", "127.0.0.1");
        var service = new UserService(
            manager.Object,
            null!,
            new UserRepository(db),
            new RoleRepository(db),
            new PermissionRepository(db),
            null!,
            new RefreshTokenRepository(db),
            new AccessContextRepository(db),
            new SavingUnitOfWork(db),
            gate.Object,
            null!,
            actor,
            sanitizer.Object,
            mapper.Object,
            new MedicalCenterRepository(db),
            new TestCenter(activeCenter, central),
            Mock.Of<IAuditLogRepository>());

        return new UserHarness(service, manager);
    }

    private static Mock<UserManager<User>> CreateUserManager()
    {
        return new Mock<UserManager<User>>(
            new Mock<IUserStore<User>>().Object,
            Mock.Of<IOptions<IdentityOptions>>(),
            Mock.Of<IPasswordHasher<User>>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<User>>>());
    }

    private static AppDbContext CreateDb()
    {
        var user = new TestCurrentUser();
        user.SetUser("actor-1", "admin", "127.0.0.1");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, user, new TestCenter(null, true), new AuditInterceptor(user));
    }

    private static async Task SeedCenterAsync(AppDbContext db, Guid id, string code)
    {
        if (await db.MedicalCenters.AnyAsync(c => c.CenterId == id))
            return;
        var now = DateTimeOffset.UtcNow;
        db.MedicalCenters.Add(new MedicalCenter
        {
            CenterId = id,
            Code = code,
            NameAr = code,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedUserAsync(AppDbContext db, string id, string userName)
    {
        if (await db.Users.AnyAsync(u => u.Id == id))
            return;
        db.Users.Add(new User
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            CreationDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedUserCenterAsync(AppDbContext db, string userId, Guid centerId)
    {
        if (await db.UserCenters.AnyAsync(x => x.UserId == userId && x.CenterId == centerId))
            return;
        db.UserCenters.Add(new UserCenter
        {
            UserId = userId,
            CenterId = centerId,
            IsActive = true,
            AssignedByUserId = userId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string> SeedRoleAsync(AppDbContext db, string name)
    {
        var existing = await db.Roles.FirstOrDefaultAsync(r => r.Name == name);
        if (existing != null)
            return existing.Id;

        var role = new Role
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            IsActive = true,
            Scope = name is "SuperAdmin" or "Auditor"
                ? Domain.Enums.RoleScope.Central
                : Domain.Enums.RoleScope.Center
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private static async Task SeedRoleLinkAsync(AppDbContext db, string userId, string roleName, Guid? centerId)
    {
        var roleId = await SeedRoleAsync(db, roleName);
        if (await db.UserRoleAssociations.AnyAsync(x =>
                x.UserId == userId && x.RoleId == roleId && x.CenterId == centerId && x.IsActive))
            return;

        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = roleId,
            CenterId = centerId,
            IsActive = true,
            AssignedByUserId = userId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedRolePermissionAsync(AppDbContext db, string roleId, string permissionName)
    {
        var permId = await SeedPermissionAsync(db, permissionName);
        if (await db.RolePermissions.AnyAsync(rp =>
                rp.RoleId == roleId && rp.PermissionId == permId && rp.IsActive))
            return;

        db.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            PermissionId = permId,
            IsActive = true,
            GrantedByUserId = "seed",
            GrantedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedPermissionAsync(AppDbContext db, string name)
    {
        var existing = await db.Permissions.FirstOrDefaultAsync(p => p.Name == name);
        if (existing != null)
            return existing.Id;

        var perm = new Permission
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = name,
            IsActive = true
        };
        db.Permissions.Add(perm);
        await db.SaveChangesAsync();
        return perm.Id;
    }

    private static async Task SeedDirectPermissionAsync(
        AppDbContext db,
        string userId,
        string permissionName,
        Guid? centerId)
    {
        var permId = await SeedPermissionAsync(db, permissionName);
        db.UserPermissions.Add(new UserPermission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PermissionId = permId,
            CenterId = centerId,
            IsActive = true,
            GrantedByUserId = userId,
            GrantedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task GrantDirectAsync(AppDbContext db, string userId, Guid permissionId, Guid centerId)
    {
        db.UserPermissions.Add(new UserPermission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PermissionId = permissionId,
            CenterId = centerId,
            IsActive = true,
            GrantedByUserId = "seed",
            GrantedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedIamKeeperAsync(AppDbContext db)
    {
        await SeedUserAsync(db, "iam-keeper", "keeper");
        var roleId = await SeedRoleAsync(db, "SuperAdmin");
        if (!await db.UserRoleAssociations.AnyAsync(x => x.UserId == "iam-keeper" && x.RoleId == roleId))
        {
            db.UserRoleAssociations.Add(new UserRoleAssociation
            {
                Id = Guid.NewGuid(),
                UserId = "iam-keeper",
                RoleId = roleId,
                CenterId = null,
                IsActive = true,
                AssignedByUserId = "iam-keeper",
                AssignedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        foreach (var permissionName in new[] { SystemPermissions.UserManagement.Read }
                     .Concat(SystemPermissions.UserManagementWrite)
                     .Concat(SystemPermissions.RoleManagementWrite)
                     .Concat(SystemPermissions.PermissionManagementWrite)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            await SeedRolePermissionAsync(db, roleId, permissionName);
    }

    private sealed record UserHarness(UserService Service, Mock<UserManager<User>> UserManager);

    private sealed class SavingUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public SavingUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new NoopTransaction());
    }

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
