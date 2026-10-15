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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Application.Tests;

public class UserAssignmentTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Assign_center_rejects_inactive_center()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ", isActive: false);
        await SeedUserAsync(db, "target-1", "nurse");
        var service = CreateService(db, unrestrictedActor: true);

        await Assert.ThrowsAsync<InactiveMedicalCenterException>(() =>
            service.AssignCenterAsync(new AssignUserCenterDto
            {
                UserId = "target-1",
                CenterId = CenterA
            }));
    }

    [Fact]
    public async Task Assign_center_rejects_duplicate_active_row_as_idempotent()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "target-1", "nurse");
        var service = CreateService(db, unrestrictedActor: true);

        var first = await service.AssignCenterAsync(new AssignUserCenterDto
        {
            UserId = "target-1",
            CenterId = CenterA
        });
        var second = await service.AssignCenterAsync(new AssignUserCenterDto
        {
            UserId = "target-1",
            CenterId = CenterA
        });

        Assert.True(first.IsActive);
        Assert.True(second.IsActive);
        Assert.Equal(1, await db.UserCenters.CountAsync(x => x.UserId == "target-1" && x.CenterId == CenterA));
    }

    [Fact]
    public async Task Local_manager_cannot_assign_outside_own_centers()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedCenterAsync(db, CenterB, "CB", "مركز ب");
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterA);
        await SeedLocalManagerRoleAsync(db, "actor-1", CenterA);

        var service = CreateService(db, unrestrictedActor: false, actorId: "actor-1");

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.AssignCenterAsync(new AssignUserCenterDto
            {
                UserId = "target-1",
                CenterId = CenterB
            }));
    }

    [Fact]
    public async Task Local_role_requires_active_center_assignment()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "target-1", "nurse");
        var roleId = await SeedRoleAsync(db, "ReceptionClerk");
        var service = CreateService(db, unrestrictedActor: true);

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.AssignRoleAsync(new AssignUserRoleDto
            {
                UserId = "target-1",
                RoleId = roleId,
                CenterId = CenterA
            }));

        Assert.Contains("إسناد فعّال", error.Message);
    }

    [Fact]
    public async Task Local_role_applies_only_in_its_center_and_not_in_another()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedCenterAsync(db, CenterB, "CB", "مركز ب");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "target-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterB);
        var roleA = await SeedRoleAsync(db, "ReceptionClerk");
        var roleB = await SeedRoleAsync(db, "ClinicDoctor");
        await SeedRolePermissionAsync(db, roleA, "Members.CreateUpdate");
        await SeedRolePermissionAsync(db, roleB, "Clinical.ExamWrite");

        var service = CreateService(db, unrestrictedActor: true);
        await service.AssignRoleAsync(new AssignUserRoleDto
        {
            UserId = "target-1",
            RoleId = roleA,
            CenterId = CenterA
        });
        await service.AssignRoleAsync(new AssignUserRoleDto
        {
            UserId = "target-1",
            RoleId = roleB,
            CenterId = CenterB
        });

        var access = new AccessContextRepository(db);
        var snapshot = await access.LoadAsync("target-1");

        var inA = CenterPermissionEvaluator.Resolve(snapshot.Roles, snapshot.Directs, snapshot.Links, CenterA);
        var inB = CenterPermissionEvaluator.Resolve(snapshot.Roles, snapshot.Directs, snapshot.Links, CenterB);

        Assert.Contains("Members.CreateUpdate", inA);
        Assert.DoesNotContain("Clinical.ExamWrite", inA);
        Assert.Contains("Clinical.ExamWrite", inB);
        Assert.DoesNotContain("Members.CreateUpdate", inB);
    }

    [Fact]
    public async Task Local_manager_cannot_assign_central_role()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterA);
        await SeedLocalManagerRoleAsync(db, "actor-1", CenterA);
        var roleId = await SeedRoleAsync(db, "ReceptionClerk");

        var service = CreateService(db, unrestrictedActor: false, actorId: "actor-1");

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.AssignRoleAsync(new AssignUserRoleDto
            {
                UserId = "target-1",
                RoleId = roleId,
                CenterId = null
            }));
    }

    [Fact]
    public async Task Assign_role_rejects_inactive_role()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "target-1", CenterA);
        var roleId = await SeedRoleAsync(db, "ReceptionClerk", isActive: false);
        var service = CreateService(db, unrestrictedActor: true);

        await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.AssignRoleAsync(new AssignUserRoleDto
            {
                UserId = "target-1",
                RoleId = roleId,
                CenterId = CenterA
            }));
    }

    [Fact]
    public async Task Revoke_last_center_requires_confirmation_and_invalidates_session()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "target-1", CenterA);
        var token = await SeedRefreshTokenAsync(db, "target-1");
        var service = CreateService(db, unrestrictedActor: true);

        var missingConfirm = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.RevokeCenterAsync(new RevokeUserCenterDto
            {
                UserId = "target-1",
                CenterId = CenterA,
                Reason = "نقل",
                ConfirmLastCenter = false
            }));
        Assert.Contains("آخر مركز", missingConfirm.Message);

        await service.RevokeCenterAsync(new RevokeUserCenterDto
        {
            UserId = "target-1",
            CenterId = CenterA,
            Reason = "نقل خارج الخدمة",
            ConfirmLastCenter = true
        });

        var row = await db.UserCenters.SingleAsync(x => x.UserId == "target-1" && x.CenterId == CenterA);
        Assert.False(row.IsActive);
        Assert.Equal("نقل خارج الخدمة", row.RevokeReason);
        Assert.NotNull(row.RevokedAt);
        Assert.True(token.IsRevoked);
        Assert.False(await new AccessContextRepository(db).IsActivelyAssignedAsync("target-1", CenterA));
    }

    [Fact]
    public async Task Get_assignments_returns_centers_and_roles_with_scope()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "target-1", CenterA);
        var roleId = await SeedRoleAsync(db, "ReceptionClerk");
        var service = CreateService(db, unrestrictedActor: true);

        await service.AssignRoleAsync(new AssignUserRoleDto
        {
            UserId = "target-1",
            RoleId = roleId,
            CenterId = CenterA
        });

        var dto = await service.GetAssignmentsAsync("target-1");
        Assert.Equal("target-1", dto.UserId);
        Assert.Contains(dto.Centers, c => c.CenterId == CenterA && c.IsActive);
        Assert.Contains(dto.Roles, r => r.RoleId == roleId && r.CenterId == CenterA && !r.IsCentralScope);
    }

    [Fact]
    public async Task Scope_guard_blocks_center_outside_actor_assignment()
    {
        var access = new Mock<IAccessContextRepository>();
        access.Setup(a => a.LoadAsync("actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot
            {
                Roles =
                {
                    new CenterPermissionEvaluator.RoleAssignment("r1", "CenterManager", CenterA, true, true)
                }
            });
        access.Setup(a => a.IsActivelyAssignedAsync("actor-1", CenterB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            UserManagementScopeGuard.EnsureCanAssignCenterAsync(access.Object, "actor-1", CenterB));
    }

    private static UserAssignmentService CreateService(
        AppDbContext db,
        bool unrestrictedActor,
        string actorId = "actor-1")
    {
        if (unrestrictedActor)
            SeedCentralActorAsync(db, actorId).GetAwaiter().GetResult();

        var users = new UserRepository(db);
        var roles = new RoleRepository(db);
        var centers = new MedicalCenterRepository(db);
        var access = new AccessContextRepository(db);
        var tokens = new RefreshTokenRepository(db);
        var uow = new TestUnitOfWork(db);

        var manager = CreateUserManager();
        manager.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .Returns((string id) => db.Users.FirstOrDefaultAsync(u => u.Id == id));
        manager.Setup(m => m.UpdateSecurityStampAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.GetPermissions()).Returns(new[]
        {
            SystemPermissions.UserManagement.Read,
            SystemPermissions.UserManagement.AssignRoles,
            MilitaryHealthPermissions.UserCenters.Manage,
            "Members.CreateUpdate",
            "Clinical.ExamWrite"
        });

        var current = new TestCurrentUser();
        current.SetUser(actorId, "admin", "127.0.0.1");

        return new UserAssignmentService(
            manager.Object,
            users,
            roles,
            centers,
            access,
            tokens,
            uow,
            gate.Object,
            current);
    }

    private static async Task SeedCentralActorAsync(AppDbContext db, string actorId)
    {
        if (await db.Users.AnyAsync(u => u.Id == actorId))
            return;

        await SeedUserAsync(db, actorId, "admin");
        var roleId = await SeedRoleAsync(db, "SuperAdmin");
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = actorId,
            RoleId = roleId,
            CenterId = null,
            IsActive = true,
            AssignedByUserId = actorId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        // حتى يُحسب SuperAdmin كمدير IAM في IamAdminGuard أثناء الاختبارات
        foreach (var permissionName in SystemPermissions.UserManagementWrite
            .Concat(SystemPermissions.RoleManagementWrite)
            .Concat(SystemPermissions.PermissionManagementWrite)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await SeedRolePermissionAsync(db, roleId, permissionName);
        }
    }

    private static async Task SeedLocalManagerRoleAsync(AppDbContext db, string userId, Guid centerId)
    {
        var roleId = await SeedRoleAsync(db, "CenterManager");
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

    private static async Task SeedCenterAsync(
        AppDbContext db,
        Guid id,
        string code,
        string nameAr,
        bool isActive = true)
    {
        if (await db.MedicalCenters.AnyAsync(c => c.CenterId == id))
            return;

        var now = DateTimeOffset.UtcNow;
        db.MedicalCenters.Add(new MedicalCenter
        {
            CenterId = id,
            Code = code,
            NameAr = nameAr,
            IsActive = isActive,
            CreatedAt = now,
            UpdatedAt = now
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

    private static async Task<string> SeedRoleAsync(AppDbContext db, string name, bool isActive = true)
    {
        var existing = await db.Roles.FirstOrDefaultAsync(r => r.Name == name);
        if (existing is not null)
        {
            existing.IsActive = isActive;
            await db.SaveChangesAsync();
            return existing.Id;
        }

        var role = new Role
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private static async Task SeedRolePermissionAsync(AppDbContext db, string roleId, string permissionName)
    {
        var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Name == permissionName);
        if (permission is null)
        {
            permission = new Permission
            {
                Id = Guid.NewGuid(),
                Name = permissionName,
                IsActive = true
            };
            db.Permissions.Add(permission);
            await db.SaveChangesAsync();
        }

        if (await db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permission.Id && rp.IsActive))
            return;

        db.RolePermissions.Add(new RolePermission
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            PermissionId = permission.Id,
            IsActive = true,
            GrantedByUserId = "actor-1",
            GrantedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<UserRefreshToken> SeedRefreshTokenAsync(AppDbContext db, string userId)
    {
        var token = new UserRefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = Guid.NewGuid().ToString("N"),
            ExpiryDate = DateTime.UtcNow.AddDays(1),
            IsRevoked = false
        };
        db.UserRefreshTokens.Add(token);
        await db.SaveChangesAsync();
        return token;
    }

    private static Mock<UserManager<User>> CreateUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(
            store.Object,
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
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, user, new TestCenter(null, true), new AuditInterceptor(user));
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public TestUnitOfWork(AppDbContext db) => _db = db;
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
}
