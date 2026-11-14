using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class UserEffectiveAccessTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Self_view_returns_active_centers_roles_and_sources()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedCenterAsync(db, CenterB, "CB", "مركز ب");
        await SeedUserAsync(db, "user-1", "doctor");
        await SeedUserCenterAsync(db, "user-1", CenterA);
        await SeedUserCenterAsync(db, "user-1", CenterB);

        var roleA = await SeedRoleAsync(db, "ClinicDoctor");
        var roleB = await SeedRoleAsync(db, "ReceptionClerk");
        await SeedRolePermissionAsync(db, roleA, "Clinical.ExamWrite");
        await SeedRolePermissionAsync(db, roleB, "Members.CreateUpdate");
        await SeedUserRoleAsync(db, "user-1", roleA, CenterA);
        await SeedUserRoleAsync(db, "user-1", roleB, CenterB);
        await SeedDirectPermissionAsync(db, "user-1", "Members.View", CenterA);

        var service = CreateService(db, actorId: "user-1", centerId: CenterA, central: false);
        var dto = await service.GetEffectiveAccessAsync("user-1", CenterA);

        Assert.True(dto.IsSelfView);
        Assert.Equal(2, dto.ActiveCenters.Count);
        Assert.Contains(dto.Roles, r => r.RoleId == roleA && r.CenterId == CenterA && !r.IsCentralScope);
        Assert.Contains(dto.Roles, r => r.RoleId == roleB && r.CenterId == CenterB);
        Assert.Contains(dto.DirectPermissions, d => d.PermissionName == "Members.View" && d.CenterId == CenterA);
        Assert.Contains(dto.EffectivePermissions, p => p.Name == "Clinical.ExamWrite" && p.FromRole && !p.FromDirect);
        Assert.Contains(dto.EffectivePermissions, p => p.Name == "Members.View" && p.FromDirect);
        Assert.DoesNotContain(dto.EffectivePermissions, p => p.Name == "Members.CreateUpdate");
        Assert.Empty(await db.AuditEvents.Where(e => e.EntityType == "UserEffectiveAccess").ToListAsync());
    }

    [Fact]
    public async Task Effective_permissions_differ_by_center()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedCenterAsync(db, CenterB, "CB", "مركز ب");
        await SeedUserAsync(db, "user-1", "doctor");
        await SeedUserCenterAsync(db, "user-1", CenterA);
        await SeedUserCenterAsync(db, "user-1", CenterB);

        var roleA = await SeedRoleAsync(db, "ClinicDoctor");
        var roleB = await SeedRoleAsync(db, "ReceptionClerk");
        await SeedRolePermissionAsync(db, roleA, "Clinical.ExamWrite");
        await SeedRolePermissionAsync(db, roleB, "Members.CreateUpdate");
        await SeedUserRoleAsync(db, "user-1", roleA, CenterA);
        await SeedUserRoleAsync(db, "user-1", roleB, CenterB);

        var service = CreateService(db, actorId: "user-1", centerId: CenterA, central: false);

        var inA = await service.GetEffectiveAccessAsync("user-1", CenterA);
        var inB = await service.GetEffectiveAccessAsync("user-1", CenterB);

        Assert.Contains(inA.EffectivePermissions, p => p.Name == "Clinical.ExamWrite");
        Assert.DoesNotContain(inA.EffectivePermissions, p => p.Name == "Members.CreateUpdate");
        Assert.Contains(inB.EffectivePermissions, p => p.Name == "Members.CreateUpdate");
        Assert.DoesNotContain(inB.EffectivePermissions, p => p.Name == "Clinical.ExamWrite");

        var byA = inA.ByCenter.Single(c => c.CenterId == CenterA);
        var byB = inA.ByCenter.Single(c => c.CenterId == CenterB);
        Assert.Contains(byA.Permissions, p => p.Name == "Clinical.ExamWrite");
        Assert.Contains(byB.Permissions, p => p.Name == "Members.CreateUpdate");
    }

    [Fact]
    public async Task Revoked_and_disabled_assignments_are_excluded()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "user-1", "doctor");
        await SeedUserCenterAsync(db, "user-1", CenterA);

        var activeRole = await SeedRoleAsync(db, "ClinicDoctor");
        var revokedRole = await SeedRoleAsync(db, "ReceptionClerk");
        await SeedRolePermissionAsync(db, activeRole, "Clinical.ExamWrite");
        await SeedRolePermissionAsync(db, revokedRole, "Members.CreateUpdate");
        await SeedUserRoleAsync(db, "user-1", activeRole, CenterA);
        await SeedUserRoleAsync(db, "user-1", revokedRole, CenterA, isActive: false);
        await SeedDirectPermissionAsync(db, "user-1", "Members.View", CenterA, isActive: false);

        var revokedCenter = await db.UserCenters.SingleAsync(x => x.UserId == "user-1" && x.CenterId == CenterA);
        // keep center active for role context; revoke a second center assignment scenario separately
        await SeedCenterAsync(db, CenterB, "CB", "مركز ب");
        db.UserCenters.Add(new UserCenter
        {
            UserId = "user-1",
            CenterId = CenterB,
            IsActive = false,
            AssignedByUserId = "user-1",
            AssignedAt = DateTimeOffset.UtcNow.AddDays(-2),
            RevokedAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedByUserId = "user-1",
            RevokeReason = "إلغاء"
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, actorId: "user-1", centerId: CenterA, central: false);
        var dto = await service.GetEffectiveAccessAsync("user-1", CenterA);

        Assert.DoesNotContain(dto.ActiveCenters, c => c.CenterId == CenterB);
        Assert.DoesNotContain(dto.Roles, r => r.RoleId == revokedRole);
        Assert.DoesNotContain(dto.DirectPermissions, d => d.PermissionName == "Members.View");
        Assert.DoesNotContain(dto.EffectivePermissions, p => p.Name == "Members.CreateUpdate");
        Assert.Contains(dto.EffectivePermissions, p => p.Name == "Clinical.ExamWrite");
        Assert.NotNull(revokedCenter);
    }

    [Fact]
    public async Task Matches_PermissionGate_resolution_for_same_snapshot()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "user-1", "doctor");
        await SeedUserCenterAsync(db, "user-1", CenterA);

        var roleId = await SeedRoleAsync(db, "ClinicDoctor");
        await SeedRolePermissionAsync(db, roleId, "Clinical.ExamWrite");
        await SeedUserRoleAsync(db, "user-1", roleId, CenterA);
        await SeedDirectPermissionAsync(db, "user-1", "Members.View", CenterA);

        var access = new AccessContextRepository(db);
        var snapshot = await access.LoadAsync("user-1");
        var gateNames = CenterPermissionEvaluator.Resolve(
            snapshot.Roles, snapshot.Directs, snapshot.Links, CenterA);

        var service = CreateService(db, actorId: "user-1", centerId: CenterA, central: false);
        var dto = await service.GetEffectiveAccessAsync("user-1", CenterA);

        Assert.Equal(gateNames, dto.EffectivePermissions.Select(p => p.Name).ToList());
    }

    [Fact]
    public async Task Local_admin_cannot_view_user_outside_scope()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedCenterAsync(db, CenterB, "CB", "مركز ب");
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterB);
        await SeedLocalManagerAsync(db, "actor-1", CenterA);

        var service = CreateService(db, actorId: "actor-1", centerId: CenterA, central: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.GetEffectiveAccessAsync("target-1", CenterB));
    }

    [Fact]
    public async Task Local_admin_sees_only_shared_center_and_writes_audit()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedCenterAsync(db, CenterB, "CB", "مركز ب");
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterB);
        await SeedLocalManagerAsync(db, "actor-1", CenterA);

        var roleA = await SeedRoleAsync(db, "ReceptionClerk");
        var roleB = await SeedRoleAsync(db, "ClinicDoctor");
        await SeedRolePermissionAsync(db, roleA, "Members.CreateUpdate");
        await SeedRolePermissionAsync(db, roleB, "Clinical.ExamWrite");
        await SeedUserRoleAsync(db, "target-1", roleA, CenterA);
        await SeedUserRoleAsync(db, "target-1", roleB, CenterB);

        var service = CreateService(db, actorId: "actor-1", centerId: CenterA, central: false);
        var dto = await service.GetEffectiveAccessAsync("target-1");

        Assert.False(dto.IsSelfView);
        Assert.Single(dto.ActiveCenters);
        Assert.Equal(CenterA, dto.ActiveCenters[0].CenterId);
        Assert.DoesNotContain(dto.Roles, r => r.CenterId == CenterB);
        Assert.Contains(dto.EffectivePermissions, p => p.Name == "Members.CreateUpdate");
        Assert.DoesNotContain(dto.EffectivePermissions, p => p.Name == "Clinical.ExamWrite");

        var audit = await db.AuditEvents.SingleAsync(e => e.EntityType == "UserEffectiveAccess");
        Assert.Equal("READ", audit.EventType);
        Assert.Equal("target-1", audit.EntityId);
        Assert.Equal("actor-1", audit.ActorUserId);
    }

    [Fact]
    public async Task Admin_without_read_permission_is_rejected()
    {
        await using var db = CreateDb();
        await SeedUserAsync(db, "actor-1", "clerk");
        await SeedUserAsync(db, "target-1", "nurse");

        var service = CreateService(
            db,
            actorId: "actor-1",
            centerId: null,
            central: true,
            configureGate: g => g.Setup(x => x.Has(It.IsAny<string>())).Returns(false));

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.GetEffectiveAccessAsync("target-1"));
    }

    [Fact]
    public async Task Auditor_permission_views_another_user_without_user_management_read()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "actor-1", "auditor");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "target-1", CenterA);
        var auditorRole = await SeedRoleAsync(db, "Auditor");
        // Scope guard reads central operation grants from DB (not the mocked gate alone).
        await SeedRolePermissionAsync(db, auditorRole, SystemPermissions.UserManagement.ViewEffectiveAccess);
        await SeedUserRoleAsync(db, "actor-1", auditorRole, null);

        var service = CreateService(
            db,
            actorId: "actor-1",
            centerId: null,
            central: true,
            configureGate: g =>
            {
                g.Setup(x => x.Has(SystemPermissions.UserManagement.Read)).Returns(false);
                g.Setup(x => x.Has(SystemPermissions.UserManagement.ViewEffectiveAccess)).Returns(true);
            });

        var dto = await service.GetEffectiveAccessAsync("target-1");

        Assert.False(dto.IsSelfView);
        Assert.Equal("target-1", dto.UserId);
        Assert.Contains(dto.ActiveCenters, c => c.CenterId == CenterA);
    }

    [Fact]
    public async Task View_permission_does_not_bypass_center_scope()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedCenterAsync(db, CenterB, "CB", "مركز ب");
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterB);
        await SeedLocalManagerAsync(db, "actor-1", CenterA);

        var service = CreateService(
            db,
            actorId: "actor-1",
            centerId: CenterA,
            central: false,
            configureGate: g =>
            {
                g.Setup(x => x.Has(SystemPermissions.UserManagement.Read)).Returns(false);
                g.Setup(x => x.Has(SystemPermissions.UserManagement.ViewEffectiveAccess)).Returns(true);
            });

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.GetEffectiveAccessAsync("target-1"));
    }

    [Fact]
    public void ResolveDetailed_sources_match_Resolve_names()
    {
        var roles = new[]
        {
            new CenterPermissionEvaluator.RoleAssignment("r1", "ClinicDoctor", CenterA, true, true)
        };
        var directs = new[]
        {
            new CenterPermissionEvaluator.DirectAssignment("Members.View", CenterA, true, true)
        };
        var links = new[]
        {
            new CenterPermissionEvaluator.RolePermissionLink("r1", "Clinical.ExamWrite", true, true),
            new CenterPermissionEvaluator.RolePermissionLink("r1", "Members.View", true, true)
        };

        var names = CenterPermissionEvaluator.Resolve(roles, directs, links, CenterA);
        var detailed = CenterPermissionEvaluator.ResolveDetailed(roles, directs, links, CenterA);

        Assert.Equal(names, detailed.Select(d => d.Name).ToList());
        var shared = detailed.Single(d => d.Name == "Members.View");
        Assert.True(shared.FromRole);
        Assert.True(shared.FromDirect);
        Assert.Contains(shared.Sources, s => s.SourceType == CenterPermissionEvaluator.SourceRole);
        Assert.Contains(shared.Sources, s => s.SourceType == CenterPermissionEvaluator.SourceDirect);
    }

    private static UserEffectiveAccessService CreateService(
        AppDbContext db,
        string actorId,
        Guid? centerId,
        bool central,
        Action<Mock<IPermissionGate>>? configureGate = null)
    {
        var users = new UserRepository(db);
        var access = new AccessContextRepository(db);
        var audit = new AuditLogRepository(db);
        var uow = new TestUnitOfWork(db);

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.Has(SystemPermissions.UserManagement.Read)).Returns(true);
        configureGate?.Invoke(gate);

        var current = new TestCurrentUser();
        current.SetUser(actorId, "actor", "127.0.0.1");

        return new UserEffectiveAccessService(
            users,
            access,
            gate.Object,
            current,
            new TestCenter(centerId, central),
            audit,
            uow);
    }

    private static async Task SeedLocalManagerAsync(AppDbContext db, string userId, Guid centerId)
    {
        var roleId = await SeedRoleAsync(db, "CenterManager");
        await SeedRolePermissionAsync(db, roleId, SystemPermissions.UserManagement.Read);
        await SeedUserRoleAsync(db, userId, roleId, centerId);
    }

    private static async Task SeedUserRoleAsync(
        AppDbContext db,
        string userId,
        string roleId,
        Guid? centerId,
        bool isActive = true)
    {
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = roleId,
            CenterId = centerId,
            IsActive = isActive,
            AssignedByUserId = userId,
            AssignedAt = DateTimeOffset.UtcNow,
            RevokedAt = isActive ? null : DateTimeOffset.UtcNow,
            RevokedByUserId = isActive ? null : userId,
            RevokeReason = isActive ? null : "إلغاء"
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedDirectPermissionAsync(
        AppDbContext db,
        string userId,
        string permissionName,
        Guid? centerId,
        bool isActive = true)
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

        db.UserPermissions.Add(new UserPermission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PermissionId = permission.Id,
            CenterId = centerId,
            IsActive = isActive,
            GrantedByUserId = userId,
            GrantedAt = DateTimeOffset.UtcNow,
            RevokedAt = isActive ? null : DateTimeOffset.UtcNow,
            RevokedByUserId = isActive ? null : userId,
            RevokeReason = isActive ? null : "إلغاء"
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
            Scope = AssignableRolePolicy.CatalogScopeForName(name),
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
