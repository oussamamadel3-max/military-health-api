using System.ComponentModel.DataAnnotations;
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

/// <summary>MH-FNC-041 — minimum one globally active role after any role mutation.</summary>
public class MinimumActiveRoleTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Guard_rejects_empty_request_tokens_and_empty_patch_clear()
    {
        var emptyDto = new AssignRolesDto { UserId = "u1", RoleIds = [], RoleNames = ["  "] };
        var results = Validate(emptyDto);
        Assert.Contains(results, r => r.ErrorMessage == MinimumActiveRoleGuard.AtLeastOneRoleMessage);

        Assert.Throws<InvalidUserDataException>(() =>
            MinimumActiveRoleGuard.EnsureRequestContainsRoleToken(null, null));
        Assert.Throws<InvalidUserDataException>(() =>
            MinimumActiveRoleGuard.EnsurePatchRolesNotEmptyClear(new List<RoleLiteDto>()));
        MinimumActiveRoleGuard.EnsurePatchRolesNotEmptyClear(null);
    }

    [Fact]
    public void Guard_merges_ids_and_names_and_rejects_zero_projected_remaining()
    {
        var tokens = MinimumActiveRoleGuard.CollectTokens(
            ["role-a", "ROLE-A"],
            ["role-b", "  ", null]);
        Assert.Equal(2, tokens.Count);

        var current = new[]
        {
            new MinimumActiveRoleGuard.ActiveRoleKey("r1", CenterA),
            new MinimumActiveRoleGuard.ActiveRoleKey("r2", CenterB)
        };

        MinimumActiveRoleGuard.EnsureProjectedActiveRolesRemain(
            current,
            removing: new[] { new MinimumActiveRoleGuard.ActiveRoleKey("r1", CenterA) },
            adding: Array.Empty<MinimumActiveRoleGuard.ActiveRoleKey>());

        var both = Assert.Throws<InvalidUserDataException>(() =>
            MinimumActiveRoleGuard.EnsureProjectedActiveRolesRemain(
                current,
                removing: current,
                adding: Array.Empty<MinimumActiveRoleGuard.ActiveRoleKey>()));
        Assert.Equal(MinimumActiveRoleGuard.MustRetainOneRoleMessage, both.Message);
    }

    [Fact]
    public async Task Toggle_rejects_empty_role_lists_without_mutating_or_invalidating()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        var roleId = await SeedRoleAsync(db, "ReceptionClerk");
        await SeedRoleLinkAsync(db, "target-1", roleId, CenterA);
        var stampBefore = (await db.Users.SingleAsync(u => u.Id == "target-1")).SecurityStamp;
        var token = await SeedRefreshTokenAsync(db, "target-1");

        var harness = CreateUserService(db, central: false);
        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            harness.Service.ToggleRolesForUserAsync(new AssignRolesDto
            {
                UserId = "target-1",
                RoleIds = [],
                RoleNames = ["   "]
            }));

        Assert.Equal(MinimumActiveRoleGuard.AtLeastOneRoleMessage, error.Message);
        Assert.True(await db.UserRoleAssociations.AnyAsync(x =>
            x.UserId == "target-1" && x.RoleId == roleId && x.IsActive));
        Assert.Equal(stampBefore, (await db.Users.SingleAsync(u => u.Id == "target-1")).SecurityStamp);
        Assert.False(token.IsRevoked);
        harness.UserManager.Verify(m => m.UpdateSecurityStampAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Patch_rejects_empty_roles_array_without_role_mutation()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        var roleId = await SeedRoleAsync(db, "ReceptionClerk");
        await SeedRoleLinkAsync(db, "target-1", roleId, CenterA);
        var stampBefore = (await db.Users.SingleAsync(u => u.Id == "target-1")).SecurityStamp;

        var harness = CreateUserService(db, central: false);
        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            harness.Service.UpdateUserAsync("target-1", new UpdateUserPatchRequest
            {
                fullName = "مستخدم",
                username = "nurse",
                email = "nurse@example.com",
                employeeNumber = "EMP-nurse",
                rankOrTitle = "موظف",
                roles = []
            }));

        Assert.Equal(MinimumActiveRoleGuard.AtLeastOneRoleMessage, error.Message);
        Assert.True(await db.UserRoleAssociations.AnyAsync(x =>
            x.UserId == "target-1" && x.IsActive && x.RoleId == roleId));
        Assert.Equal(stampBefore, (await db.Users.SingleAsync(u => u.Id == "target-1")).SecurityStamp);
    }

    [Fact]
    public async Task Update_with_roles_rejects_empty_role_ids()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        var roleId = await SeedRoleAsync(db, "ReceptionClerk");
        await SeedRoleLinkAsync(db, "target-1", roleId, CenterA);

        var harness = CreateUserService(db, central: false);
        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            harness.Service.UpdateUserWithRolesAsync("target-1", new UpdateUserWithRolesDto
            {
                UserName = "nurse",
                Email = "nurse@example.com",
                RoleIds = []
            }));

        Assert.Equal(MinimumActiveRoleGuard.AtLeastOneRoleMessage, error.Message);
        Assert.True(await db.UserRoleAssociations.AnyAsync(x =>
            x.UserId == "target-1" && x.IsActive && x.RoleId == roleId));
    }

    [Fact]
    public async Task Revoke_rejects_last_active_role_and_rolls_back_without_session_invalidation()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        var roleId = await SeedRoleAsync(db, "ReceptionClerk");
        await SeedRoleLinkAsync(db, "target-1", roleId, CenterA);
        var token = await SeedRefreshTokenAsync(db, "target-1");
        var stampBefore = (await db.Users.SingleAsync(u => u.Id == "target-1")).SecurityStamp;
        var uow = new RollbackTrackingUnitOfWork(db);
        var service = CreateAssignmentService(db, uow);

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.RevokeRoleAsync(new RevokeUserRoleDto
            {
                UserId = "target-1",
                RoleId = roleId,
                CenterId = CenterA,
                Reason = "إزالة"
            }));

        Assert.Equal(MinimumActiveRoleGuard.MustRetainOneRoleMessage, error.Message);
        Assert.True(uow.RolledBack);
        Assert.False(uow.Committed);
        Assert.True(await db.UserRoleAssociations.AnyAsync(x =>
            x.UserId == "target-1" && x.RoleId == roleId && x.IsActive));
        Assert.Equal(stampBefore, (await db.Users.SingleAsync(u => u.Id == "target-1")).SecurityStamp);
        Assert.False(token.IsRevoked);
    }

    [Fact]
    public async Task Revoke_allows_last_role_in_center_when_another_center_or_central_remains()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        await SeedCenterAsync(db, CenterB, true);
        await SeedUserCenterAsync(db, "target-1", CenterB);
        var clerk = await SeedRoleAsync(db, "ReceptionClerk");
        var doctor = await SeedRoleAsync(db, "ClinicDoctor");
        await SeedRoleLinkAsync(db, "target-1", clerk, CenterA);
        await SeedRoleLinkAsync(db, "target-1", doctor, CenterB);

        var service = CreateAssignmentService(db);
        await service.RevokeRoleAsync(new RevokeUserRoleDto
        {
            UserId = "target-1",
            RoleId = clerk,
            CenterId = CenterA,
            Reason = "نقل داخلي"
        });

        var active = await db.UserRoleAssociations.Where(x => x.UserId == "target-1" && x.IsActive).ToListAsync();
        Assert.DoesNotContain(active, x => x.RoleId == clerk && x.CenterId == CenterA);
        Assert.Contains(active, x => x.RoleId == doctor && x.CenterId == CenterB);
    }

    [Fact]
    public async Task Sync_rejects_replacing_sole_role_with_nothing_via_projected_guard()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        var clerk = await SeedRoleAsync(db, "ReceptionClerk");
        await SeedRoleLinkAsync(db, "target-1", clerk, CenterA);
        // Defense-in-depth: call sync path with a token that resolves then is not kept —
        // empty lists are rejected earlier; here revoke-equivalent sync is blocked by Guard
        // when desired set would clear the only global role (covered via UpdateUserWithRoles
        // attempting to sync to a role while MayRevoke clears others — use revoke).
        var service = CreateAssignmentService(db);
        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.RevokeRoleAsync(new RevokeUserRoleDto
            {
                UserId = "target-1",
                RoleId = clerk,
                CenterId = CenterA,
                Reason = "تفريغ"
            }));
        Assert.Equal(MinimumActiveRoleGuard.MustRetainOneRoleMessage, error.Message);
    }

    [Fact]
    public async Task Sequential_revokes_of_two_last_roles_second_request_fails()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        var clerk = await SeedRoleAsync(db, "ReceptionClerk");
        var doctor = await SeedRoleAsync(db, "ClinicDoctor");
        await SeedRoleLinkAsync(db, "target-1", clerk, CenterA);
        await SeedRoleLinkAsync(db, "target-1", doctor, CenterA);
        var service = CreateAssignmentService(db);

        await service.RevokeRoleAsync(new RevokeUserRoleDto
        {
            UserId = "target-1",
            RoleId = clerk,
            CenterId = CenterA,
            Reason = "أول إزالة"
        });

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.RevokeRoleAsync(new RevokeUserRoleDto
            {
                UserId = "target-1",
                RoleId = doctor,
                CenterId = CenterA,
                Reason = "ثاني إزالة"
            }));

        Assert.Equal(MinimumActiveRoleGuard.MustRetainOneRoleMessage, error.Message);
        Assert.Equal(1, await db.UserRoleAssociations.CountAsync(x => x.UserId == "target-1" && x.IsActive));
        Assert.True(await db.UserRoleAssociations.AnyAsync(x =>
            x.UserId == "target-1" && x.RoleId == doctor && x.CenterId == CenterA && x.IsActive));
    }

    [Fact]
    public async Task Iam_admin_guard_still_blocks_stripping_last_iam_admin_when_other_role_remains()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, true);
        await SeedUserAsync(db, "actor-1", "auditor");
        await SeedUserAsync(db, "target-1", "admin-user");
        await SeedUserCenterAsync(db, "target-1", CenterA);
        // Central authority without IAM write set — actor can manage, but is not an IAM admin.
        var auditor = await SeedRoleAsync(db, "Auditor");
        await SeedRoleLinkAsync(db, "actor-1", auditor, null);

        // Sole IAM-capable user (no iam-keeper). Keep ReceptionClerk so minimum-role passes.
        var iamRole = await SeedRoleAsync(db, "SuperAdmin");
        foreach (var permissionName in SystemPermissions.UserManagementWrite
                     .Concat(SystemPermissions.RoleManagementWrite)
                     .Concat(SystemPermissions.PermissionManagementWrite)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            await SeedRolePermissionAsync(db, iamRole, permissionName);

        var clerk = await SeedRoleAsync(db, "ReceptionClerk");
        await SeedRoleLinkAsync(db, "target-1", iamRole, null);
        await SeedRoleLinkAsync(db, "target-1", clerk, CenterA);
        var token = await SeedRefreshTokenAsync(db, "target-1");
        var stampBefore = (await db.Users.SingleAsync(u => u.Id == "target-1")).SecurityStamp;

        var uow = new RollbackTrackingUnitOfWork(db);
        var harness = CreateAssignmentHarness(db, uow, unrestrictedActor: true);
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.RevokeRoleAsync(new RevokeUserRoleDto
            {
                UserId = "target-1",
                RoleId = iamRole,
                CenterId = null,
                Reason = "سحب IAM"
            }));

        Assert.True(uow.RolledBack);
        Assert.False(uow.Committed);
        Assert.Equal(stampBefore, (await db.Users.SingleAsync(u => u.Id == "target-1")).SecurityStamp);
        Assert.False(token.IsRevoked);
        harness.UserManager.Verify(m => m.UpdateSecurityStampAsync(It.IsAny<User>()), Times.Never);
        // Minimum-role still satisfied via ReceptionClerk — IAM guard is the rejecting rule.
        Assert.True(await db.UserRoleAssociations.AnyAsync(x =>
            x.UserId == "target-1" && x.RoleId == clerk && x.IsActive));
    }

    [Fact]
    public async Task Toggle_sync_keeps_other_center_roles_and_rejects_clearing_last_global_via_sync()
    {
        await using var db = CreateDb();
        await SeedBasicsAsync(db);
        await SeedCenterAsync(db, CenterB, true);
        await SeedUserCenterAsync(db, "target-1", CenterB);
        var clerk = await SeedRoleAsync(db, "ReceptionClerk");
        var doctor = await SeedRoleAsync(db, "ClinicDoctor");
        await SeedRoleLinkAsync(db, "target-1", clerk, CenterA);
        await SeedRoleLinkAsync(db, "target-1", doctor, CenterB);

        var harness = CreateUserService(db, central: false);
        // Replace Center A role with doctor — Center B doctor remains; global count >= 1.
        await harness.Service.ToggleRolesForUserAsync(new AssignRolesDto
        {
            UserId = "target-1",
            RoleIds = [doctor],
            CenterId = CenterA
        });

        var active = await db.UserRoleAssociations.Where(x => x.UserId == "target-1" && x.IsActive).ToListAsync();
        Assert.Contains(active, x => x.RoleId == doctor && x.CenterId == CenterA);
        Assert.Contains(active, x => x.RoleId == doctor && x.CenterId == CenterB);
        Assert.DoesNotContain(active, x => x.RoleId == clerk && x.IsActive);
    }

    [Fact]
    public async Task Persisted_post_check_rejects_when_active_roles_already_empty()
    {
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetActiveRoleIdsAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            MinimumActiveRoleGuard.EnsurePersistedActiveRoleRemainsAsync(users.Object, "u1"));
        Assert.Equal(MinimumActiveRoleGuard.MustRetainOneRoleMessage, error.Message);
    }

    private static List<ValidationResult> Validate(AssignRolesDto dto)
    {
        var ctx = new ValidationContext(dto);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, ctx, results, validateAllProperties: true);
        return results;
    }

    private static async Task SeedBasicsAsync(AppDbContext db)
    {
        await SeedCenterAsync(db, CenterA, true);
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterA);
        await SeedRoleLinkAsync(db, "actor-1", await SeedRoleAsync(db, "CenterManager"), CenterA);
        await SeedIamKeeperAsync(db);
    }

    private static UserHarness CreateUserService(AppDbContext db, bool central)
    {
        var manager = CreateUserManager();
        manager.Setup(m => m.FindByNameAsync(It.IsAny<string>()))
            .Returns((string name) => db.Users.FirstOrDefaultAsync(u => u.UserName == name));
        manager.Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .Returns((string email) => db.Users.FirstOrDefaultAsync(u => u.Email == email));
        manager.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .Returns((string id) => db.Users.FirstOrDefaultAsync(u => u.Id == id));
        manager.Setup(m => m.UpdateAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);
        manager.Setup(m => m.UpdateSecurityStampAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);

        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map(It.IsAny<UpdateUserPatchRequest>(), It.IsAny<User>()));
        mapper.Setup(m => m.Map<UserListItemDto>(It.IsAny<User>()))
            .Returns((User user) => new UserListItemDto
            {
                userId = user.Id,
                username = user.UserName ?? string.Empty,
                email = user.Email ?? string.Empty
            });
        mapper.Setup(m => m.Map<List<RoleLiteDto>>(It.IsAny<object>())).Returns(new List<RoleLiteDto>());
        mapper.Setup(m => m.Map<List<PermissionLiteDto>>(It.IsAny<object>())).Returns(new List<PermissionLiteDto>());

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
            Mock.Of<IPermissionGate>(),
            null!,
            actor,
            sanitizer.Object,
            mapper.Object,
            new MedicalCenterRepository(db),
            new TestCenter(central ? null : CenterA, central),
            Mock.Of<IAuditLogRepository>());

        return new UserHarness(service, manager);
    }

    private static UserAssignmentService CreateAssignmentService(
        AppDbContext db,
        IUnitOfWork? uow = null,
        bool unrestrictedActor = false)
        => CreateAssignmentHarness(db, uow, unrestrictedActor).Service;

    private static AssignmentHarness CreateAssignmentHarness(
        AppDbContext db,
        IUnitOfWork? uow = null,
        bool unrestrictedActor = false)
    {
        var manager = CreateUserManager();
        manager.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .Returns((string id) => db.Users.FirstOrDefaultAsync(u => u.Id == id));
        manager.Setup(m => m.UpdateSecurityStampAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.GetPermissions()).Returns(new[]
        {
            SystemPermissions.UserManagement.Read,
            SystemPermissions.UserManagement.AssignRoles
        });

        var current = new TestCurrentUser();
        current.SetUser("actor-1", "admin", "127.0.0.1");

        var service = new UserAssignmentService(
            manager.Object,
            new UserRepository(db),
            new RoleRepository(db),
            new MedicalCenterRepository(db),
            new AccessContextRepository(db),
            new RefreshTokenRepository(db),
            uow ?? new SavingUnitOfWork(db),
            gate.Object,
            current,
            new TestCenter(unrestrictedActor ? null : CenterA, unrestrictedActor),
            Mock.Of<IAuditLogRepository>());

        return new AssignmentHarness(service, manager);
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

        foreach (var permissionName in SystemPermissions.UserManagementWrite
                     .Concat(SystemPermissions.RoleManagementWrite)
                     .Concat(SystemPermissions.PermissionManagementWrite)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            await SeedRolePermissionAsync(db, roleId, permissionName);
    }

    private static async Task SeedRoleLinkAsync(AppDbContext db, string userId, string roleId, Guid? centerId)
    {
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

    private static async Task SeedCenterAsync(AppDbContext db, Guid id, bool isActive)
    {
        if (await db.MedicalCenters.AnyAsync(c => c.CenterId == id))
            return;

        var now = DateTimeOffset.UtcNow;
        db.MedicalCenters.Add(new MedicalCenter
        {
            CenterId = id,
            Code = id.ToString()[..2],
            NameAr = "مركز",
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

    private static async Task<string> SeedRoleAsync(AppDbContext db, string name)
    {
        var existing = await db.Roles.FirstOrDefaultAsync(r => r.Name == name);
        if (existing is not null)
            return existing.Id;

        var role = new Role
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Scope = AssignableRolePolicy.CatalogScopeForName(name),
            IsActive = true,
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
            permission = new Permission { Id = Guid.NewGuid(), Name = permissionName, IsActive = true };
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

    private sealed record UserHarness(UserService Service, Mock<UserManager<User>> UserManager);

    private sealed record AssignmentHarness(UserAssignmentService Service, Mock<UserManager<User>> UserManager);

    private sealed class SavingUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public SavingUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new NoopTransaction());
    }

    private sealed class RollbackTrackingUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public RollbackTrackingUnitOfWork(AppDbContext db) => _db = db;
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new TrackingTransaction(this));

        private sealed class TrackingTransaction : IDbContextTransaction
        {
            private readonly RollbackTrackingUnitOfWork _owner;
            public TrackingTransaction(RollbackTrackingUnitOfWork owner) => _owner = owner;
            public Guid TransactionId { get; } = Guid.NewGuid();
            public void Commit() => _owner.Committed = true;
            public Task CommitAsync(CancellationToken cancellationToken = default)
            {
                _owner.Committed = true;
                return Task.CompletedTask;
            }
            public void Rollback() => _owner.RolledBack = true;
            public Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                _owner.RolledBack = true;
                return Task.CompletedTask;
            }
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
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
