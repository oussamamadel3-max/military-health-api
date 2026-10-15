using Application.Common.Permissions;
using Application.Common.Security;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Medical;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace Application.Tests;

public class IamHardeningTests
{
    [Fact]
    public async Task Delete_user_deactivates_the_row_and_invalidates_the_session()
    {
        var user = new User { Id = "user-1", UserName = "doctor", IsActive = true, SecurityStamp = "stamp-1" };
        var token = new UserRefreshToken { Id = Guid.NewGuid(), UserId = user.Id, Token = "hash", ExpiryDate = DateTime.UtcNow.AddDays(1) };
        var manager = CreateUserManager();
        manager.Setup(m => m.FindByIdAsync(user.Id)).ReturnsAsync(user);
        manager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        manager.Setup(m => m.UpdateSecurityStampAsync(user)).ReturnsAsync(IdentityResult.Success);
        manager.Setup(m => m.DeleteAsync(It.IsAny<User>())).ThrowsAsync(new InvalidOperationException("physical delete"));

        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetDirectPermissionNamesAsync(user.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        users.Setup(r => r.GetActiveRoleIdsAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        users.Setup(r => r.FindByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRolePermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePermission>());
        var tokens = new Mock<IRefreshTokenRepository>();
        tokens.Setup(r => r.GetActiveByUserIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<UserRefreshToken> { token });
        tokens.Setup(r => r.MarkRevoked(It.IsAny<UserRefreshToken>())).Callback<UserRefreshToken>(item =>
        {
            item.IsRevoked = true;
            item.RevokedAt = DateTimeOffset.UtcNow;
        });
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var gate = new Mock<IPermissionGate>();

        var access = new Mock<IAccessContextRepository>();
        access.Setup(a => a.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot
            {
                Roles =
                {
                    new CenterPermissionEvaluator.RoleAssignment(
                        "role-central", "SuperAdmin", null, true, true)
                }
            });

        var actor = new TestCurrentUser();
        actor.SetUser("actor-1", "admin", "127.0.0.1");

        var service = new UserService(
            manager.Object, null!, users.Object, roles.Object, null!, null!, tokens.Object, access.Object, uow.Object,
            gate.Object, null!, actor, null!, null!);

        await service.Delete(user.Id);

        Assert.False(user.IsActive);
        Assert.True(token.IsRevoked);
        Assert.NotNull(token.RevokedAt);
        manager.Verify(m => m.DeleteAsync(It.IsAny<User>()), Times.Never);
        manager.Verify(m => m.UpdateSecurityStampAsync(user), Times.Once);
    }

    [Fact]
    public async Task Delete_role_deactivates_the_row()
    {
        var role = new Role { Id = "role-1", Name = "Nurse", IsActive = true };
        var store = new Mock<IRoleStore<Role>>();
        var manager = new Mock<RoleManager<Role>>(store.Object, Array.Empty<IRoleValidator<Role>>(), new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), Mock.Of<ILogger<RoleManager<Role>>>());
        manager.Setup(m => m.FindByIdAsync(role.Id)).ReturnsAsync(role);
        manager.Setup(m => m.UpdateAsync(role)).ReturnsAsync(IdentityResult.Success);
        manager.Setup(m => m.DeleteAsync(It.IsAny<Role>())).ThrowsAsync(new InvalidOperationException("physical delete"));

        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetUserIdsByRoleIdAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        users.Setup(r => r.GetActiveUserIdsExceptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRolePermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePermission>());
        var service = new RoleService(
            manager.Object, users.Object, null!, null!, new TestCurrentUser(), Mock.Of<IPermissionGate>(),
            roles.Object, Mock.Of<IPermissionRepository>(), Mock.Of<IRefreshTokenRepository>(), Mock.Of<IUnitOfWork>());

        await service.DeleteRoleAsync(role.Id);

        Assert.False(role.IsActive);
        Assert.NotNull(role.UpdatedAt);
        manager.Verify(m => m.DeleteAsync(It.IsAny<Role>()), Times.Never);
    }

    [Fact]
    public async Task Toggle_permission_status_deactivates_without_removing_the_row()
    {
        var permission = new Permission { Id = Guid.NewGuid(), Name = "UserManagement.Read", IsActive = true };
        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(r => r.FindByIdAsync(permission.Id, It.IsAny<CancellationToken>())).ReturnsAsync(permission);
        permissions.Setup(r => r.GetRoleIdsByPermissionIdAsync(permission.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        permissions.Setup(r => r.GetUserIdsByPermissionIdAsync(permission.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        permissions.Setup(r => r.Remove(It.IsAny<Permission>())).Throws(new InvalidOperationException("physical delete"));
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var service = new PermissionService(
            permissions.Object, Mock.Of<IRoleRepository>(), Mock.Of<IUserRepository>(), Mock.Of<IRefreshTokenRepository>(), uow.Object,
            null!, null!, Mock.Of<IPermissionGate>(), new TestCurrentUser(), null!);

        await service.TogglePermissionStatusAsync(permission.Id);

        Assert.False(permission.IsActive);
        permissions.Verify(r => r.Update(permission), Times.Once);
        permissions.Verify(r => r.Remove(It.IsAny<Permission>()), Times.Never);
    }

    [Fact]
    public async Task Withdrawing_assignments_revokes_them_and_a_new_grant_reactivates_the_same_row()
    {
        await using var db = CreateDb();
        var roleLink = new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            RoleId = "role-1",
            AssignedByUserId = "actor-1",
            AssignedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            IsActive = true
        };
        var direct = new UserPermission
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            PermissionId = Guid.NewGuid(),
            GrantedByUserId = "actor-1",
            GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            IsActive = true
        };
        var rolePermission = new RolePermission
        {
            Id = Guid.NewGuid(),
            RoleId = "role-1",
            PermissionId = direct.PermissionId,
            GrantedByUserId = "actor-1",
            GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            IsActive = true
        };
        var center = new UserCenter
        {
            UserId = "user-1",
            CenterId = Guid.NewGuid(),
            AssignedByUserId = "actor-1",
            AssignedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            IsActive = true
        };
        var clinic = new UserClinicAssignment
        {
            AssignmentId = Guid.NewGuid(),
            UserId = "user-1",
            CenterId = center.CenterId,
            ClinicId = Guid.NewGuid(),
            AssignedByUserId = "actor-1",
            AssignedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            IsActive = true
        };
        db.AddRange(roleLink, direct, rolePermission, center, clinic);
        await db.SaveChangesAsync();

        IamRetention.Revoke(roleLink, "actor-1", IamRetention.RoleWithdrawn);
        IamRetention.Revoke(direct, "actor-1", IamRetention.DirectPermissionWithdrawn);
        IamRetention.Revoke(rolePermission, "actor-1", IamRetention.RolePermissionWithdrawn);
        IamRetention.Revoke(center, "actor-1", IamRetention.CenterRevoked);
        IamRetention.Revoke(clinic, "actor-1", IamRetention.ClinicRevoked);
        await db.SaveChangesAsync();

        Assert.All(new object[] { roleLink, direct, rolePermission, center, clinic }, row =>
        {
            var active = (bool)row.GetType().GetProperty("IsActive")!.GetValue(row)!;
            Assert.False(active);
            Assert.Equal("actor-1", row.GetType().GetProperty("RevokedByUserId")!.GetValue(row));
            Assert.NotNull(row.GetType().GetProperty("RevokedAt")!.GetValue(row));
            Assert.False(string.IsNullOrWhiteSpace((string?)row.GetType().GetProperty("RevokeReason")!.GetValue(row)));
        });
        Assert.Equal(5, await db.AuditEvents.CountAsync(e => e.EventType == "Modified"));

        var users = new UserRepository(db);
        users.AddRoleAssociations(new[]
        {
            new UserRoleAssociation
            {
                UserId = roleLink.UserId,
                RoleId = roleLink.RoleId,
                AssignedByUserId = "actor-2",
                AssignedAt = DateTimeOffset.UtcNow,
                IsActive = true
            }
        });
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.UserRoleAssociations.CountAsync());
        Assert.True(roleLink.IsActive);
        Assert.Null(roleLink.RevokedAt);
        Assert.Null(roleLink.RevokeReason);
        Assert.Equal("actor-2", roleLink.AssignedByUserId);
    }

    [Fact]
    public void Revoked_assignments_grant_no_permissions()
    {
        var names = CenterPermissionEvaluator.Resolve(
            new[] { new CenterPermissionEvaluator.RoleAssignment("role-1", "Nurse", null, false, true) },
            new[] { new CenterPermissionEvaluator.DirectAssignment("Dashboard.Read", null, false, true) },
            new[] { new CenterPermissionEvaluator.RolePermissionLink("role-1", "UserManagement.Read", false, true) },
            null);

        Assert.Empty(names);
    }

    [Fact]
    public async Task Second_obsolete_cleanup_keeps_one_inactive_permission_and_its_history()
    {
        await using var db = CreateDb();
        var permission = new Permission { Id = Guid.NewGuid(), Name = "OrgStructure.Read", IsActive = true };
        var roleLink = new RolePermission
        {
            RoleId = "role-1",
            PermissionId = permission.Id,
            GrantedByUserId = "actor-1",
            GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            IsActive = true
        };
        var direct = new UserPermission
        {
            UserId = "user-1",
            PermissionId = permission.Id,
            GrantedByUserId = "actor-1",
            GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            IsActive = true
        };
        db.AddRange(permission, roleLink, direct);
        await db.SaveChangesAsync();

        for (var pass = 0; pass < 2; pass++)
        {
            if (permission.IsActive)
                IamRetention.DeactivatePermission(permission);
            IamRetention.Revoke(roleLink, "actor-1", IamRetention.ObsoletePermission);
            IamRetention.Revoke(direct, "actor-1", IamRetention.ObsoletePermission);
            await db.SaveChangesAsync();
        }

        Assert.False(permission.IsActive);
        Assert.Equal(1, await db.Permissions.CountAsync());
        Assert.Equal(1, await db.RolePermissions.CountAsync());
        Assert.Equal(1, await db.UserPermissions.CountAsync());
        Assert.False(roleLink.IsActive);
        Assert.False(direct.IsActive);
        Assert.DoesNotContain(db.ChangeTracker.Entries(), entry => entry.State == EntityState.Deleted);
    }

    [Fact]
    public async Task Deactivation_and_revocation_are_written_to_audit()
    {
        await using var db = CreateDb();
        var permission = new Permission { Id = Guid.NewGuid(), Name = "Dashboard.Read", IsActive = true };
        db.Add(permission);
        await db.SaveChangesAsync();
        var created = await db.AuditEvents.CountAsync(e => e.EntityType == nameof(Permission) && e.EventType == "Added");

        IamRetention.DeactivatePermission(permission);
        await db.SaveChangesAsync();

        Assert.Equal(1, created);
        Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.EntityType == nameof(Permission) && e.EventType == "Modified"));
    }

    [Fact]
    public async Task Soft_deleted_file_hides_its_attachment_without_hiding_the_user_profile()
    {
        await using var db = CreateDb();
        var visible = NewFile(false);
        var hidden = NewFile(true);
        var visibleAttachment = NewAttachment(visible.FileAssetId);
        var hiddenAttachment = NewAttachment(hidden.FileAssetId);
        var profile = new UserProfile
        {
            UserId = "user-1",
            EmployeeNumber = "E-1",
            RankOrTitle = "Captain",
            UpdatedByUserId = "actor-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ProfileFileAssetId = hidden.FileAssetId
        };
        db.AddRange(visible, hidden, visibleAttachment, hiddenAttachment, profile);
        await db.SaveChangesAsync();

        var files = await db.FileAssets.Select(f => f.FileAssetId).ToListAsync();
        var attachments = await db.MedicalAttachments.Select(a => a.AttachmentId).ToListAsync();
        var profiles = await db.UserProfiles.ToListAsync();
        var auditFiles = await db.FileAssets.IgnoreQueryFilters().CountAsync();
        var auditAttachments = await db.MedicalAttachments.IgnoreQueryFilters().CountAsync();

        Assert.Equal(new[] { visible.FileAssetId }, files);
        Assert.Equal(new[] { visibleAttachment.AttachmentId }, attachments);
        var loadedProfile = Assert.Single(profiles);
        Assert.Equal(hidden.FileAssetId, loadedProfile.ProfileFileAssetId);
        Assert.Equal(2, auditFiles);
        Assert.Equal(2, auditAttachments);
    }

    [Fact]
    public async Task Postgres_rejects_direct_delete_on_protected_iam_tables()
    {
        var connectionString = Environment.GetEnvironmentVariable("MHS_HARDENING_PG");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.Database, "MilitaryHealthHardeningValidation", StringComparison.Ordinal))
            throw new InvalidOperationException("The trigger test refuses every database except MilitaryHealthHardeningValidation.");

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        foreach (var (table, probe) in DeleteProbes)
        {
            await using var transaction = await connection.BeginTransactionAsync();
            var rejected = false;
            try
            {
                await Execute(connection, transaction, probe);
                await Execute(connection, transaction, $"DELETE FROM {table} WHERE {ProbePredicate(table)}");
            }
            catch (PostgresException ex) when (ex.SqlState == "P0001")
            {
                rejected = ex.MessageText.Contains("cannot be physically deleted", StringComparison.Ordinal)
                    && ex.MessageText.Contains("deactivate/revoke", StringComparison.Ordinal);
            }
            finally
            {
                await transaction.RollbackAsync();
            }

            Assert.True(rejected, table);
        }
    }

    private static string ProbePredicate(string table) => table switch
    {
        "iam.\"Users\"" => "\"Id\" = 'hardening-probe-user'",
        "iam.\"Roles\"" => "\"Id\" = 'hardening-probe-role'",
        "iam.\"Permissions\"" => "\"Name\" = 'Hardening.Probe'",
        "iam.\"RolePermissions\"" => "\"RoleId\" = 'hardening-probe-role' AND \"PermissionId\" = '11111111-1111-1111-1111-111111111111'",
        "iam.\"UserRoleAssociations\"" => "\"UserId\" = 'hardening-probe-user' AND \"RoleId\" = 'hardening-probe-role'",
        "iam.\"UserPermissions\"" => "\"UserId\" = 'hardening-probe-user' AND \"PermissionId\" = '11111111-1111-1111-1111-111111111111'",
        "iam.\"userCenter\"" => "\"userId\" = 'hardening-probe-user'",
        "iam.\"userClinicAssignment\"" => "\"userId\" = 'hardening-probe-user'",
        _ => throw new InvalidOperationException(table)
    };

    private static readonly (string Table, string Probe)[] DeleteProbes =
    {
        ("iam.\"Users\"", """
            INSERT INTO iam."Users" ("Id", "UserName", "NormalizedUserName", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount", "IsActive", "LoginLockoutTier", "CreationDate")
            VALUES ('hardening-probe-user', 'hardening-probe', 'HARDENING-PROBE', false, false, false, false, 0, true, 0, now())
            """),
        ("iam.\"Roles\"", """
            INSERT INTO iam."Roles" ("Id", "Name", "NormalizedName", "IsActive", "CreatedAt")
            VALUES ('hardening-probe-role', 'HardeningProbe', 'HARDENINGPROBE', true, now())
            """),
        ("iam.\"Permissions\"", """
            INSERT INTO iam."Permissions" ("Id", "Name", "IsActive")
            VALUES ('11111111-1111-1111-1111-111111111111', 'Hardening.Probe', true)
            """),
        ("iam.\"RolePermissions\"", """
            INSERT INTO iam."Users" ("Id", "UserName", "NormalizedUserName", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount", "IsActive", "LoginLockoutTier", "CreationDate")
            VALUES ('hardening-probe-user', 'hardening-probe', 'HARDENING-PROBE', false, false, false, false, 0, true, 0, now());
            INSERT INTO iam."Roles" ("Id", "Name", "NormalizedName", "IsActive", "CreatedAt")
            VALUES ('hardening-probe-role', 'HardeningProbe', 'HARDENINGPROBE', true, now());
            INSERT INTO iam."Permissions" ("Id", "Name", "IsActive")
            VALUES ('11111111-1111-1111-1111-111111111111', 'Hardening.Probe', true);
            INSERT INTO iam."RolePermissions" ("Id", "RoleId", "PermissionId", "IsActive", "GrantedByUserId", "GrantedAt")
            VALUES ('22222222-2222-2222-2222-222222222222', 'hardening-probe-role', '11111111-1111-1111-1111-111111111111', true, 'hardening-probe-user', now())
            """),
        ("iam.\"UserRoleAssociations\"", """
            INSERT INTO iam."Users" ("Id", "UserName", "NormalizedUserName", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount", "IsActive", "LoginLockoutTier", "CreationDate")
            VALUES ('hardening-probe-user', 'hardening-probe', 'HARDENING-PROBE', false, false, false, false, 0, true, 0, now());
            INSERT INTO iam."Roles" ("Id", "Name", "NormalizedName", "IsActive", "CreatedAt")
            VALUES ('hardening-probe-role', 'HardeningProbe', 'HARDENINGPROBE', true, now());
            INSERT INTO iam."UserRoleAssociations" ("Id", "UserId", "RoleId", "IsActive", "AssignedByUserId", "AssignedAt")
            VALUES ('33333333-3333-3333-3333-333333333333', 'hardening-probe-user', 'hardening-probe-role', true, 'hardening-probe-user', now())
            """),
        ("iam.\"UserPermissions\"", """
            INSERT INTO iam."Users" ("Id", "UserName", "NormalizedUserName", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount", "IsActive", "LoginLockoutTier", "CreationDate")
            VALUES ('hardening-probe-user', 'hardening-probe', 'HARDENING-PROBE', false, false, false, false, 0, true, 0, now());
            INSERT INTO iam."Permissions" ("Id", "Name", "IsActive")
            VALUES ('11111111-1111-1111-1111-111111111111', 'Hardening.Probe', true);
            INSERT INTO iam."UserPermissions" ("Id", "UserId", "PermissionId", "IsActive", "GrantedByUserId", "GrantedAt")
            VALUES ('44444444-4444-4444-4444-444444444444', 'hardening-probe-user', '11111111-1111-1111-1111-111111111111', true, 'hardening-probe-user', now())
            """),
        ("iam.\"userCenter\"", """
            INSERT INTO iam."Users" ("Id", "UserName", "NormalizedUserName", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount", "IsActive", "LoginLockoutTier", "CreationDate")
            VALUES ('hardening-probe-user', 'hardening-probe', 'HARDENING-PROBE', false, false, false, false, 0, true, 0, now());
            INSERT INTO core."medicalCenter" ("centerId", "code", "nameAr", "isActive", "createdAt", "updatedAt")
            VALUES ('55555555-5555-5555-5555-555555555555', 'HARDENING', 'مركز التحقق', true, now(), now());
            INSERT INTO iam."userCenter" ("userId", "centerId", "isActive", "assignedByUserId", "assignedAt")
            VALUES ('hardening-probe-user', '55555555-5555-5555-5555-555555555555', true, 'hardening-probe-user', now())
            """),
        ("iam.\"userClinicAssignment\"", """
            INSERT INTO iam."Users" ("Id", "UserName", "NormalizedUserName", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount", "IsActive", "LoginLockoutTier", "CreationDate")
            VALUES ('hardening-probe-user', 'hardening-probe', 'HARDENING-PROBE', false, false, false, false, 0, true, 0, now());
            INSERT INTO core."medicalCenter" ("centerId", "code", "nameAr", "isActive", "createdAt", "updatedAt")
            VALUES ('55555555-5555-5555-5555-555555555555', 'HARDENING', 'مركز التحقق', true, now(), now());
            INSERT INTO iam."userCenter" ("userId", "centerId", "isActive", "assignedByUserId", "assignedAt")
            VALUES ('hardening-probe-user', '55555555-5555-5555-5555-555555555555', true, 'hardening-probe-user', now());
            INSERT INTO core."clinic" ("clinicId", "code", "nameAr", "sortOrder", "countsForFinal", "isActive")
            VALUES ('66666666-6666-6666-6666-666666666666', 'HARDENING-CLINIC', 'عيادة التحقق', 32000, true, true);
            INSERT INTO core."centerClinic" ("centerClinicId", "centerId", "clinicId", "isActive", "activatedAt")
            VALUES ('77777777-7777-7777-7777-777777777777', '55555555-5555-5555-5555-555555555555', '66666666-6666-6666-6666-666666666666', true, now());
            INSERT INTO iam."userClinicAssignment" ("assignmentId", "userId", "centerId", "clinicId", "isActive", "assignedAt", "assignedByUserId")
            VALUES ('88888888-8888-8888-8888-888888888888', 'hardening-probe-user', '55555555-5555-5555-5555-555555555555', '66666666-6666-6666-6666-666666666666', true, now(), 'hardening-probe-user')
            """)
    };

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        foreach (var statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await using var command = new NpgsqlCommand(statement, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
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
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, user, new TestCenter(null, true), new AuditInterceptor(user));
    }

    private static FileAsset NewFile(bool deleted) => new()
    {
        FileAssetId = Guid.NewGuid(),
        CenterId = Guid.NewGuid(),
        StorageKey = "storage/" + Guid.NewGuid().ToString("N"),
        OriginalName = "scan.pdf",
        MimeType = "application/pdf",
        Extension = ".pdf",
        SizeBytes = 128,
        UploadedByUserId = "actor-1",
        CreatedAt = DateTimeOffset.UtcNow,
        IsDeleted = deleted
    };

    private static MedicalAttachment NewAttachment(Guid fileId) => new()
    {
        AttachmentId = Guid.NewGuid(),
        MemberId = Guid.NewGuid(),
        CenterId = Guid.NewGuid(),
        FileAssetId = fileId,
        ConsultationId = Guid.NewGuid(),
        UploadedByUserId = "actor-1",
        CreatedAt = DateTimeOffset.UtcNow
    };
}
