using Application.Common.Permissions;
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
/// FR-018 assignable-permissions read path: AssignPermissions only, no PermissionManagement.Read.
/// </summary>
public class AssignablePermissionsTests
{
    private static readonly Guid Idlib = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Damascus = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Local_manager_lists_assignable_within_active_center()
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
        var createId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.Create);
        var elevatedId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Centers.Manage);
        await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.Update); // not in actor gate
        await GrantDirectAsync(db, "target-1", createId, Idlib);
        await GrantDirectAsync(db, "target-1", elevatedId, Idlib);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View,
            MilitaryHealthPermissions.Members.Create);
        var harness = CreateUserService(db, Idlib, central: false, gate);

        var dto = await harness.Service.GetAssignablePermissionsAsync("target-1", centerId: null);

        Assert.Equal("target-1", dto.UserId);
        Assert.Equal(Idlib, dto.ScopeCenterId);
        Assert.Contains(dto.Assignable, p => p.Id == viewId && p.CanGrant && p.CanRevoke);
        Assert.Contains(dto.Assignable, p => p.Id == createId);
        Assert.DoesNotContain(dto.Assignable, p => p.Id == elevatedId);
        Assert.DoesNotContain(dto.Assignable, p => p.Name == MilitaryHealthPermissions.Members.Update);

        var locked = Assert.Single(dto.CurrentDirect, p => p.Id == elevatedId);
        Assert.True(locked.IsLocked);
        Assert.False(locked.CanRevoke);
        Assert.False(locked.WithinActorDelegation);

        var editable = Assert.Single(dto.CurrentDirect, p => p.Id == createId);
        Assert.False(editable.IsLocked);
        Assert.True(editable.CanRevoke);
        Assert.True(editable.WithinActorDelegation);
    }

    [Fact]
    public async Task Local_manager_rejects_target_outside_shared_center()
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
        await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, Idlib, central: false, gate);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.GetAssignablePermissionsAsync("target-dam", Idlib));
    }

    [Fact]
    public async Task Local_null_centerId_resolves_to_active_center_not_central()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedUserAsync(db, "actor-1", "mgr");
        await SeedUserAsync(db, "target-1", "user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-1", Idlib);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);
        await SeedDirectPermissionAsync(db, "actor-1", SystemPermissions.UserManagement.Read, centerId: null);
        await SeedIamKeeperAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        await GrantDirectAsync(db, "target-1", viewId, Idlib);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            SystemPermissions.UserManagement.Read,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, Idlib, central: false, gate);

        var dto = await harness.Service.GetAssignablePermissionsAsync("target-1", centerId: null);

        Assert.Equal(Idlib, dto.ScopeCenterId);
        Assert.Contains(dto.CurrentDirect, p => p.Id == viewId);
    }

    [Fact]
    public async Task Central_read_does_not_expand_local_assign_to_foreign_center_or_central_scope()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedCenterAsync(db, Damascus, "DAM");
        await SeedUserAsync(db, "actor-1", "mgr");
        await SeedUserAsync(db, "target-1", "user");
        await SeedUserAsync(db, "target-dam", "dam.user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-1", Idlib);
        await SeedUserCenterAsync(db, "target-dam", Damascus);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);
        await SeedDirectPermissionAsync(db, "actor-1", SystemPermissions.UserManagement.Read, centerId: null);
        await SeedIamKeeperAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        await GrantDirectAsync(db, "target-1", viewId, Idlib);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            SystemPermissions.UserManagement.Read,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, Idlib, central: false, gate);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.GetAssignablePermissionsAsync("target-dam", Damascus));

        var dto = await harness.Service.GetAssignablePermissionsAsync("target-1", centerId: null);
        Assert.Equal(Idlib, dto.ScopeCenterId);
        Assert.NotNull(dto.ScopeCenterId);
    }

    [Fact]
    public async Task Central_actor_null_centerId_keeps_central_scope()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedUserAsync(db, "actor-1", "central.admin");
        await SeedUserAsync(db, "target-1", "user");
        await SeedUserCenterAsync(db, "target-1", Idlib);
        await SeedRoleLinkAsync(db, "actor-1", "SuperAdmin", null);
        await SeedRolePermissionAsync(
            db,
            await db.Roles.Where(r => r.Name == "SuperAdmin").Select(r => r.Id).SingleAsync(),
            SystemPermissions.UserManagement.AssignPermissions);
        await SeedIamKeeperAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        await GrantCentralDirectAsync(db, "target-1", viewId);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, activeCenter: null, central: true, gate);

        var dto = await harness.Service.GetAssignablePermissionsAsync("target-1", centerId: null);

        Assert.Null(dto.ScopeCenterId);
        Assert.Contains(dto.Assignable, p => p.Id == viewId);
        Assert.Contains(dto.CurrentDirect, p => p.Id == viewId && !p.IsLocked);
    }

    [Fact]
    public async Task Missing_assign_permissions_is_rejected()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedUserAsync(db, "actor-1", "mgr");
        await SeedUserAsync(db, "target-1", "user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-1", Idlib);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.GetPermissions()).Returns(new[] { MilitaryHealthPermissions.Members.View });
        gate.Setup(g => g.EnsureAll(It.IsAny<string[]>()))
            .Callback((string[] required) =>
            {
                if (required.Contains(SystemPermissions.UserManagement.AssignPermissions, StringComparer.OrdinalIgnoreCase))
                    throw new ForbiddenPermissionException("ليس لديك الصلاحية المطلوبة.");
            });

        var harness = CreateUserService(db, Idlib, central: false, gate);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.GetAssignablePermissionsAsync("target-1"));
    }

    [Fact]
    public async Task Inactive_catalog_permission_is_not_assignable()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedUserAsync(db, "actor-1", "mgr");
        await SeedUserAsync(db, "target-1", "user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-1", Idlib);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);
        await SeedIamKeeperAsync(db);

        var activeId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        var inactiveId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.Create, isActive: false);

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View,
            MilitaryHealthPermissions.Members.Create);
        var harness = CreateUserService(db, Idlib, central: false, gate);

        var dto = await harness.Service.GetAssignablePermissionsAsync("target-1", Idlib);

        Assert.Contains(dto.Assignable, p => p.Id == activeId);
        Assert.DoesNotContain(dto.Assignable, p => p.Id == inactiveId);
    }

    [Fact]
    public async Task Read_path_does_not_mutate_permissions_or_security_stamp()
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
        var stampBefore = (await db.Users.SingleAsync(u => u.Id == "target-1")).SecurityStamp;
        var permCountBefore = await db.UserPermissions.CountAsync();

        var gate = Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View);
        var harness = CreateUserService(db, Idlib, central: false, gate);

        _ = await harness.Service.GetAssignablePermissionsAsync("target-1", Idlib);

        Assert.Equal(permCountBefore, await db.UserPermissions.CountAsync());
        Assert.Equal(stampBefore, (await db.Users.SingleAsync(u => u.Id == "target-1")).SecurityStamp);
        harness.UserManager.Verify(m => m.UpdateSecurityStampAsync(It.IsAny<User>()), Times.Never);
    }

    #region Harness

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
            Id = Guid.NewGuid(),
            RoleId = roleId,
            PermissionId = permId,
            IsActive = true,
            GrantedByUserId = "actor-1",
            GrantedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedPermissionAsync(AppDbContext db, string name, bool isActive = true)
    {
        var existing = await db.Permissions.FirstOrDefaultAsync(p => p.Name == name);
        if (existing != null)
        {
            existing.IsActive = isActive;
            await db.SaveChangesAsync();
            return existing.Id;
        }

        var perm = new Permission
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = name,
            IsActive = isActive
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

    private static async Task GrantCentralDirectAsync(AppDbContext db, string userId, Guid permissionId)
    {
        db.UserPermissions.Add(new UserPermission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PermissionId = permissionId,
            CenterId = null,
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
