using System.Text.Json;
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
/// MH-IAM-SEC-03 — شفافية السحب غير المفوّض في SyncDirectPermissions وتوحيد معنى centerId:null.
/// </summary>
public class IamSec03DirectPermissionSyncTests
{
    private static readonly Guid Idlib = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Damascus = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Sync_all_revokes_authorized_returns_empty_skipped_revokes()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        await GrantDirectAsync(db, "target-1", viewId, Idlib);

        var harness = CreateUserService(db, Idlib, false, Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View));

        var result = await harness.Service.SyncDirectPermissionsAsync(new SyncDirectPermissionsDto
        {
            UserId = "target-1",
            PermissionIds = [],
            CenterId = null
        });

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Removed);
        Assert.True(result.SessionsInvalidated);
        Assert.Empty(result.SkippedRevokes);
    }

    [Fact]
    public async Task Sync_out_of_delegation_revoke_stays_active_and_appears_in_skipped_revokes()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        var elevatedId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Centers.Manage);
        await GrantDirectAsync(db, "target-1", elevatedId, Idlib);

        var harness = CreateUserService(db, Idlib, false, Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View));

        var result = await harness.Service.SyncDirectPermissionsAsync(new SyncDirectPermissionsDto
        {
            UserId = "target-1",
            PermissionIds = [],
            CenterId = Idlib
        });

        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Removed);
        Assert.False(result.SessionsInvalidated);
        var skipped = Assert.Single(result.SkippedRevokes);
        Assert.Equal(elevatedId, skipped.PermissionId);
        Assert.Equal(MilitaryHealthPermissions.Centers.Manage, skipped.Name);
        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == elevatedId && p.IsActive));
    }

    [Fact]
    public async Task Sync_mixed_revokes_executes_authorized_and_reports_unauthorized()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        var elevatedId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Centers.Manage);
        await GrantDirectAsync(db, "target-1", viewId, Idlib);
        await GrantDirectAsync(db, "target-1", elevatedId, Idlib);

        var harness = CreateUserService(db, Idlib, false, Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View));

        var result = await harness.Service.SyncDirectPermissionsAsync(new SyncDirectPermissionsDto
        {
            UserId = "target-1",
            PermissionIds = [],
            CenterId = Idlib
        });

        Assert.Equal(1, result.Removed);
        Assert.Equal(0, result.Added);
        Assert.True(result.SessionsInvalidated);
        Assert.Equal(elevatedId, Assert.Single(result.SkippedRevokes).PermissionId);
        Assert.False(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == viewId && p.IsActive));
        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == elevatedId && p.IsActive));
    }

    [Fact]
    public async Task Sync_only_unauthorized_revokes_reports_skips_without_session_invalidation()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        var elevatedId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Centers.Manage);
        await GrantDirectAsync(db, "target-1", elevatedId, Idlib);

        var stamps = 0;
        var harness = CreateUserService(db, Idlib, false, Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View));
        harness.UserManager.Setup(m => m.UpdateSecurityStampAsync(It.IsAny<User>()))
            .Callback(() => stamps++)
            .ReturnsAsync(IdentityResult.Success);

        var result = await harness.Service.SyncDirectPermissionsAsync(new SyncDirectPermissionsDto
        {
            UserId = "target-1",
            PermissionIds = []
        });

        Assert.Equal(0, result.Removed);
        Assert.False(result.SessionsInvalidated);
        Assert.Equal(0, stamps);
        Assert.Single(result.SkippedRevokes);
    }

    [Fact]
    public async Task Sync_rejects_add_beyond_actor_without_partial_writes()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        var elevatedId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Centers.Manage);
        await GrantDirectAsync(db, "target-1", viewId, Idlib);

        var harness = CreateUserService(db, Idlib, false, Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View));

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.SyncDirectPermissionsAsync(new SyncDirectPermissionsDto
            {
                UserId = "target-1",
                PermissionIds = [viewId, elevatedId]
            }));

        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == viewId && p.IsActive));
        Assert.Equal(0, await db.UserPermissions.CountAsync(p =>
            p.UserId == "target-1" && p.PermissionId == elevatedId && p.IsActive));
    }

    [Fact]
    public async Task Sync_skipped_revokes_are_deduplicated_and_do_not_include_kept_grants()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        var elevatedId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Centers.Manage);
        await GrantDirectAsync(db, "target-1", viewId, Idlib);
        await GrantDirectAsync(db, "target-1", elevatedId, Idlib);

        var harness = CreateUserService(db, Idlib, false, Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View));

        var result = await harness.Service.SyncDirectPermissionsAsync(new SyncDirectPermissionsDto
        {
            UserId = "target-1",
            PermissionIds = [viewId]
        });

        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Removed);
        Assert.False(result.SessionsInvalidated);
        Assert.Equal(elevatedId, Assert.Single(result.SkippedRevokes).PermissionId);
        Assert.DoesNotContain(result.SkippedRevokes, s => s.PermissionId == viewId);
    }

    [Fact]
    public async Task Sync_does_not_report_skips_for_other_center_or_other_user_grants()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        await SeedCenterAsync(db, Damascus, "DAM");
        await SeedUserAsync(db, "other-1", "other");
        await SeedUserCenterAsync(db, "other-1", Damascus);
        var elevatedId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Centers.Manage);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);
        await GrantDirectAsync(db, "target-1", viewId, Idlib);
        await GrantDirectAsync(db, "target-1", elevatedId, Damascus);
        await GrantDirectAsync(db, "other-1", elevatedId, Damascus);

        var harness = CreateUserService(db, Idlib, false, Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View));

        var result = await harness.Service.SyncDirectPermissionsAsync(new SyncDirectPermissionsDto
        {
            UserId = "target-1",
            PermissionIds = [],
            CenterId = Idlib
        });

        Assert.Equal(1, result.Removed);
        Assert.Empty(result.SkippedRevokes);
        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == elevatedId && p.CenterId == Damascus && p.IsActive));
        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "other-1" && p.PermissionId == elevatedId && p.IsActive));
    }

    [Fact]
    public async Task Local_null_centerId_uses_active_center_and_rejects_foreign_center()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        await SeedCenterAsync(db, Damascus, "DAM");
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);

        var harness = CreateUserService(db, Idlib, false, Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View));

        var added = await harness.Service.SyncDirectPermissionsAsync(new SyncDirectPermissionsDto
        {
            UserId = "target-1",
            PermissionIds = [viewId],
            CenterId = null
        });
        Assert.Equal(1, added.Added);
        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == viewId && p.CenterId == Idlib && p.IsActive));

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.SyncDirectPermissionsAsync(new SyncDirectPermissionsDto
            {
                UserId = "target-1",
                PermissionIds = [viewId],
                CenterId = Damascus
            }));
    }

    [Fact]
    public async Task Local_assign_with_central_read_cannot_create_central_direct_grant_via_null()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        await SeedDirectPermissionAsync(db, "actor-1", SystemPermissions.UserManagement.Read, centerId: null);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);

        var harness = CreateUserService(db, Idlib, false, Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            SystemPermissions.UserManagement.Read,
            MilitaryHealthPermissions.Members.View));

        var result = await harness.Service.SyncDirectPermissionsAsync(new SyncDirectPermissionsDto
        {
            UserId = "target-1",
            PermissionIds = [viewId],
            CenterId = null
        });

        Assert.Equal(1, result.Added);
        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == viewId && p.CenterId == Idlib && p.IsActive));
        Assert.False(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == viewId && p.CenterId == null && p.IsActive));
    }

    [Fact]
    public async Task Central_operation_actor_null_centerId_keeps_central_direct_scope()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        await SeedDirectPermissionAsync(db, "actor-1", SystemPermissions.UserManagement.AssignPermissions, centerId: null);
        var viewId = await SeedPermissionAsync(db, MilitaryHealthPermissions.Members.View);

        var harness = CreateUserService(db, activeCenter: null, central: true, Gate(
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.Members.View));

        var result = await harness.Service.SyncDirectPermissionsAsync(new SyncDirectPermissionsDto
        {
            UserId = "target-1",
            PermissionIds = [viewId],
            CenterId = null
        });

        Assert.Equal(1, result.Added);
        Assert.True(await db.UserPermissions.AnyAsync(p =>
            p.UserId == "target-1" && p.PermissionId == viewId && p.CenterId == null && p.IsActive));
    }

    [Fact]
    public void Direct_permission_sync_result_json_keeps_legacy_fields_and_adds_skipped_revokes()
    {
        var dto = new DirectPermissionSyncResultDto
        {
            Added = 1,
            Removed = 2,
            SessionsInvalidated = true,
            SkippedRevokes =
            {
                new SkippedDirectPermissionRevokeDto
                {
                    PermissionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = "Centers.Manage"
                }
            }
        };

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("added").GetInt32());
        Assert.Equal(2, root.GetProperty("removed").GetInt32());
        Assert.True(root.GetProperty("sessionsInvalidated").GetBoolean());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("skippedRevokes").ValueKind);
        Assert.Equal("Centers.Manage", root.GetProperty("skippedRevokes")[0].GetProperty("name").GetString());
        Assert.False(root.TryGetProperty("permissionIds", out _));
    }

    [Fact]
    public void CenterId_xml_docs_do_not_claim_null_always_means_central_assignment()
    {
        AssertSourceDocsContain(
            Path.Combine("Application", "Dtos", "AuthDtos", "PermissionDto.cs"),
            "للمنفّذ المحلي: <c>null</c> أو غياب القيمة يعني استخدام المركز النشط الذي يتحقق منه الخادم، وليس إنشاء إسناد مركزي");
        AssertSourceDocsContain(
            Path.Combine("Application", "Dtos", "AuthDtos", "UserAssignmentDtos.cs"),
            "وليس إنشاء إسناد مركزي");
        AssertSourceDocsContain(
            Path.Combine("Application", "Dtos", "AuthDtos", "AssignRolesDto.cs"),
            "وليس إنشاء إسناد مركزي");

        var assignRoleDocs = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "Application", "Dtos", "AuthDtos", "UserAssignmentDtos.cs"));
        Assert.DoesNotContain("فارغ = إسناد مركزي عام", assignRoleDocs);
    }

    #region helpers

    private static void AssertSourceDocsContain(string relativePath, string expectedSnippet)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        var text = File.ReadAllText(path);
        Assert.Contains(expectedSnippet, text);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Backend.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Backend.sln not found from test base directory.");
    }

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
            new Mock<IMapper>().Object,
            new MedicalCenterRepository(db),
            new TestCenter(activeCenter, central),
            Mock.Of<IAuditLogRepository>());

        return new UserHarness(service, manager);
    }

    private static Mock<UserManager<User>> CreateUserManager() =>
        new(new Mock<IUserStore<User>>().Object,
            Mock.Of<IOptions<IdentityOptions>>(),
            Mock.Of<IPasswordHasher<User>>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<User>>>());

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

    private static async Task SeedBasicsAsync(AppDbContext db)
    {
        await SeedCenterAsync(db, Idlib, "IDB");
        await SeedUserAsync(db, "actor-1", "mgr");
        await SeedUserAsync(db, "target-1", "user");
        await SeedUserCenterAsync(db, "actor-1", Idlib);
        await SeedUserCenterAsync(db, "target-1", Idlib);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", Idlib);
        await SeedIamKeeperAsync(db);
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
            AssignedByUserId = userId,
            AssignedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedIamKeeperAsync(AppDbContext db)
    {
        await SeedUserAsync(db, "iam-keeper", "keeper");
        var roleId = await SeedRoleAsync(db, "SuperAdmin");
        if (!await db.UserRoleAssociations.AnyAsync(x => x.UserId == "iam-keeper" && x.RoleId == roleId && x.IsActive))
        {
            db.UserRoleAssociations.Add(new UserRoleAssociation
            {
                Id = Guid.NewGuid(),
                UserId = "iam-keeper",
                RoleId = roleId,
                CenterId = null,
                AssignedByUserId = "iam-keeper",
                AssignedAt = DateTimeOffset.UtcNow,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        foreach (var permissionName in new[] { SystemPermissions.UserManagement.Read }
                     .Concat(SystemPermissions.UserManagementWrite)
                     .Concat(SystemPermissions.RoleManagementWrite)
                     .Concat(SystemPermissions.PermissionManagementWrite)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var permissionId = await SeedPermissionAsync(db, permissionName);
            if (await db.RolePermissions.AnyAsync(rp =>
                    rp.RoleId == roleId && rp.PermissionId == permissionId && rp.IsActive))
                continue;

            db.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId,
                GrantedByUserId = "iam-keeper",
                GrantedAt = DateTimeOffset.UtcNow,
                IsActive = true
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedPermissionAsync(AppDbContext db, string name)
    {
        var existing = await db.Permissions.FirstOrDefaultAsync(p => p.Name == name);
        if (existing != null)
            return existing.Id;
        var permission = new Permission { Id = Guid.NewGuid(), Name = name, IsActive = true };
        db.Permissions.Add(permission);
        await db.SaveChangesAsync();
        return permission.Id;
    }

    private static async Task SeedDirectPermissionAsync(AppDbContext db, string userId, string name, Guid? centerId)
    {
        var permissionId = await SeedPermissionAsync(db, name);
        if (await db.UserPermissions.AnyAsync(p =>
                p.UserId == userId && p.PermissionId == permissionId && p.CenterId == centerId && p.IsActive))
            return;
        db.UserPermissions.Add(new UserPermission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PermissionId = permissionId,
            CenterId = centerId,
            GrantedByUserId = userId,
            GrantedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private static async Task GrantDirectAsync(AppDbContext db, string userId, Guid permissionId, Guid? centerId)
    {
        db.UserPermissions.Add(new UserPermission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PermissionId = permissionId,
            CenterId = centerId,
            GrantedByUserId = "actor-1",
            GrantedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private sealed record UserHarness(UserService Service, Mock<UserManager<User>> UserManager);

    private sealed class SavingUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public SavingUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => _db.Database.BeginTransactionAsync(cancellationToken);
    }

    #endregion
}
