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

public class UserClinicAssignmentTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Assign_two_active_doctors_to_same_clinic()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedDoctorAsync(db, "doc-1", "doctor1", CenterA);
        await SeedDoctorAsync(db, "doc-2", "doctor2", CenterA);
        var service = CreateService(db, unrestrictedActor: true);

        var a = await service.AssignAsync(new AssignUserClinicDto
        {
            UserId = "doc-1",
            CenterId = CenterA,
            ClinicId = ClinicEyes
        });
        var b = await service.AssignAsync(new AssignUserClinicDto
        {
            UserId = "doc-2",
            CenterId = CenterA,
            ClinicId = ClinicEyes
        });

        Assert.True(a.IsActive);
        Assert.True(b.IsActive);
        Assert.Equal(2, await db.UserClinicAssignments.CountAsync(x =>
            x.CenterId == CenterA && x.ClinicId == ClinicEyes && x.IsActive));
    }

    [Fact]
    public async Task Assign_one_doctor_to_two_clinics_in_same_center()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedClinicAsync(db, ClinicEnt, "ENT", "الأذنية");
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedCenterClinicAsync(db, CenterA, ClinicEnt);
        await SeedDoctorAsync(db, "doc-1", "doctor1", CenterA);
        var service = CreateService(db, unrestrictedActor: true);

        await service.AssignAsync(new AssignUserClinicDto
        {
            UserId = "doc-1",
            CenterId = CenterA,
            ClinicId = ClinicEyes
        });
        await service.AssignAsync(new AssignUserClinicDto
        {
            UserId = "doc-1",
            CenterId = CenterA,
            ClinicId = ClinicEnt
        });

        Assert.Equal(2, await db.UserClinicAssignments.CountAsync(x =>
            x.UserId == "doc-1" && x.CenterId == CenterA && x.IsActive));
    }

    [Fact]
    public async Task Assign_rejects_clinic_of_another_center()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedCenterAsync(db, CenterB, "CB", "مركز ب");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedCenterClinicAsync(db, CenterB, ClinicEyes);
        await SeedDoctorAsync(db, "doc-1", "doctor1", CenterA);
        var service = CreateService(db, unrestrictedActor: true);

        await Assert.ThrowsAsync<CenterClinicNotFoundException>(() =>
            service.AssignAsync(new AssignUserClinicDto
            {
                UserId = "doc-1",
                CenterId = CenterA,
                ClinicId = ClinicEyes
            }));
    }

    [Fact]
    public async Task Assign_rejects_inactive_center_clinic()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes, isActive: false);
        await SeedDoctorAsync(db, "doc-1", "doctor1", CenterA);
        var service = CreateService(db, unrestrictedActor: true);

        await Assert.ThrowsAsync<InactiveCenterClinicException>(() =>
            service.AssignAsync(new AssignUserClinicDto
            {
                UserId = "doc-1",
                CenterId = CenterA,
                ClinicId = ClinicEyes
            }));
    }

    [Fact]
    public async Task Assign_rejects_inactive_doctor()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedDoctorAsync(db, "doc-1", "doctor1", CenterA, userIsActive: false);
        var service = CreateService(db, unrestrictedActor: true);

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.AssignAsync(new AssignUserClinicDto
            {
                UserId = "doc-1",
                CenterId = CenterA,
                ClinicId = ClinicEyes
            }));
        Assert.Contains("غير فعّال", error.Message);
    }

    [Fact]
    public async Task Assign_rejects_doctor_without_active_center_assignment()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedUserAsync(db, "doc-1", "doctor1");
        var roleId = await SeedRoleAsync(db, "ClinicDoctor");
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = "doc-1",
            RoleId = roleId,
            CenterId = CenterA,
            IsActive = true,
            AssignedByUserId = "doc-1",
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, unrestrictedActor: true);

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.AssignAsync(new AssignUserClinicDto
            {
                UserId = "doc-1",
                CenterId = CenterA,
                ClinicId = ClinicEyes
            }));
        Assert.Contains("إسناد فعّال إلى المركز", error.Message);
    }

    [Fact]
    public async Task Assign_rejects_non_doctor_role()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedUserAsync(db, "clerk-1", "clerk1");
        await SeedUserCenterAsync(db, "clerk-1", CenterA);
        var roleId = await SeedRoleAsync(db, "ReceptionClerk");
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = "clerk-1",
            RoleId = roleId,
            CenterId = CenterA,
            IsActive = true,
            AssignedByUserId = "clerk-1",
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, unrestrictedActor: true);

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.AssignAsync(new AssignUserClinicDto
            {
                UserId = "clerk-1",
                CenterId = CenterA,
                ClinicId = ClinicEyes
            }));
        Assert.Contains("طبيب العيادة", error.Message);
    }

    [Fact]
    public async Task Assign_rejects_duplicate_active_assignment()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedDoctorAsync(db, "doc-1", "doctor1", CenterA);
        var service = CreateService(db, unrestrictedActor: true);

        await service.AssignAsync(new AssignUserClinicDto
        {
            UserId = "doc-1",
            CenterId = CenterA,
            ClinicId = ClinicEyes
        });

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.AssignAsync(new AssignUserClinicDto
            {
                UserId = "doc-1",
                CenterId = CenterA,
                ClinicId = ClinicEyes
            }));
        Assert.Contains("مكرر", error.Message);
        Assert.Equal(1, await db.UserClinicAssignments.CountAsync(x =>
            x.UserId == "doc-1" && x.ClinicId == ClinicEyes && x.IsActive));
    }

    [Fact]
    public async Task Concurrent_duplicate_assign_is_rejected_consistently()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedDoctorAsync(db, "doc-1", "doctor1", CenterA);
        var service = CreateService(db, unrestrictedActor: true);

        var dto = new AssignUserClinicDto
        {
            UserId = "doc-1",
            CenterId = CenterA,
            ClinicId = ClinicEyes
        };

        await service.AssignAsync(dto);
        await Assert.ThrowsAsync<InvalidUserDataException>(() => service.AssignAsync(dto));
        Assert.Equal(1, await db.UserClinicAssignments.CountAsync(x =>
            x.UserId == "doc-1" && x.ClinicId == ClinicEyes && x.IsActive));
    }

    [Fact]
    public async Task Revoke_keeps_history_and_excludes_from_active_list()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedDoctorAsync(db, "doc-1", "doctor1", CenterA);
        var service = CreateService(db, unrestrictedActor: true);

        var assigned = await service.AssignAsync(new AssignUserClinicDto
        {
            UserId = "doc-1",
            CenterId = CenterA,
            ClinicId = ClinicEyes
        });

        await service.RevokeAsync(new RevokeUserClinicDto
        {
            AssignmentId = assigned.AssignmentId,
            Reason = "نقل تخصص"
        });

        var row = await db.UserClinicAssignments.SingleAsync(x => x.AssignmentId == assigned.AssignmentId);
        Assert.False(row.IsActive);
        Assert.Equal("نقل تخصص", row.RevokeReason);
        Assert.NotNull(row.RevokedAt);

        var active = await service.GetClinicDoctorsAsync(CenterA, ClinicEyes, 1, 10, activeOnly: true);
        Assert.Empty(active.Items);

        var all = await service.GetUserClinicAssignmentsAsync("doc-1", activeOnly: false);
        Assert.Contains(all, x => x.AssignmentId == assigned.AssignmentId && !x.IsActive);
    }

    [Fact]
    public async Task Reassign_after_revoke_reactivates_same_natural_key()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedDoctorAsync(db, "doc-1", "doctor1", CenterA);
        var service = CreateService(db, unrestrictedActor: true);

        var first = await service.AssignAsync(new AssignUserClinicDto
        {
            UserId = "doc-1",
            CenterId = CenterA,
            ClinicId = ClinicEyes
        });
        await service.RevokeAsync(new RevokeUserClinicDto
        {
            AssignmentId = first.AssignmentId,
            Reason = "إيقاف مؤقت"
        });

        var second = await service.AssignAsync(new AssignUserClinicDto
        {
            UserId = "doc-1",
            CenterId = CenterA,
            ClinicId = ClinicEyes
        });

        Assert.True(second.IsActive);
        Assert.Equal(first.AssignmentId, second.AssignmentId);
        Assert.Null(second.RevokedAt);
        Assert.Equal(1, await db.UserClinicAssignments.CountAsync(x =>
            x.UserId == "doc-1" && x.ClinicId == ClinicEyes));
    }

    [Fact]
    public async Task Local_manager_cannot_manage_other_center_assignments()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedCenterAsync(db, CenterB, "CB", "مركز ب");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedCenterClinicAsync(db, CenterB, ClinicEyes);
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedLocalManagerRoleAsync(db, "actor-1", CenterA);
        await SeedDoctorAsync(db, "doc-1", "doctor1", CenterB);

        var service = CreateService(db, unrestrictedActor: false, actorId: "actor-1");

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.AssignAsync(new AssignUserClinicDto
            {
                UserId = "doc-1",
                CenterId = CenterB,
                ClinicId = ClinicEyes
            }));
    }

    [Fact]
    public async Task Unauthorized_permission_gate_blocks_assign()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedDoctorAsync(db, "doc-1", "doctor1", CenterA);

        var service = CreateService(db, unrestrictedActor: true, grantClinicManage: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.AssignAsync(new AssignUserClinicDto
            {
                UserId = "doc-1",
                CenterId = CenterA,
                ClinicId = ClinicEyes
            }));
    }

    [Fact]
    public async Task Listing_center_clinics_and_doctors_stays_in_scope()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون");
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedDoctorAsync(db, "doc-1", "doctor1", CenterA);
        var service = CreateService(db, unrestrictedActor: true);

        await service.AssignAsync(new AssignUserClinicDto
        {
            UserId = "doc-1",
            CenterId = CenterA,
            ClinicId = ClinicEyes
        });

        var clinics = await service.GetCenterClinicsAsync(CenterA, 1, 10);
        Assert.Contains(clinics.Items, c => c.ClinicId == ClinicEyes && c.ActiveDoctorsCount == 1);

        var doctors = await service.GetCenterDoctorsAsync(CenterA, 1, 10);
        Assert.Contains(doctors.Items, d => d.UserId == "doc-1" && d.ActiveClinicAssignmentsCount == 1);
    }

    [Fact]
    public async Task Seed_catalog_includes_user_clinics_manage_once()
    {
        var codes = MilitaryHealthSeedCatalog.DomainPermissions
            .Select(p => p.Name)
            .Where(n => n == MilitaryHealthPermissions.UserClinics.Manage)
            .ToList();
        Assert.Single(codes);

        var byRole = MilitaryHealthSeedCatalog.GrantsByRoleName();
        Assert.Contains(MilitaryHealthPermissions.UserClinics.Manage, byRole["SuperAdmin"]);
        Assert.Contains(MilitaryHealthPermissions.UserClinics.Manage, byRole["CenterManager"]);
        Assert.DoesNotContain(MilitaryHealthPermissions.UserClinics.Manage, byRole["ClinicDoctor"]);
    }

    private static UserClinicAssignmentService CreateService(
        AppDbContext db,
        bool unrestrictedActor,
        string actorId = "actor-1",
        bool grantClinicManage = true)
    {
        if (unrestrictedActor)
            SeedCentralActorAsync(db, actorId).GetAwaiter().GetResult();

        var users = new UserRepository(db);
        var centers = new MedicalCenterRepository(db);
        var centerClinics = new CenterClinicRepository(db);
        var access = new AccessContextRepository(db);
        var uow = new TestUnitOfWork(db);

        var manager = CreateUserManager();
        manager.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .Returns((string id) => db.Users.FirstOrDefaultAsync(u => u.Id == id));

        var permissions = new List<string>
        {
            SystemPermissions.UserManagement.Read,
            MilitaryHealthPermissions.UserCenters.Manage
        };
        if (grantClinicManage)
            permissions.Add(MilitaryHealthPermissions.UserClinics.Manage);

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.GetPermissions()).Returns(permissions.ToArray());
        gate.Setup(g => g.EnsureAll(It.IsAny<string[]>()))
            .Callback((string[] required) =>
            {
                if (required.Any(r => !permissions.Contains(r, StringComparer.Ordinal)))
                    throw new ForbiddenPermissionException();
            });

        var current = new TestCurrentUser();
        current.SetUser(actorId, "admin", "127.0.0.1");

        return new UserClinicAssignmentService(
            manager.Object,
            users,
            centers,
            centerClinics,
            access,
            uow,
            gate.Object,
            current);
    }

    private static async Task SeedDoctorAsync(
        AppDbContext db,
        string userId,
        string userName,
        Guid centerId,
        bool userIsActive = true)
    {
        await SeedUserAsync(db, userId, userName, userIsActive);
        await SeedUserCenterAsync(db, userId, centerId);
        var roleId = await SeedRoleAsync(db, "ClinicDoctor");
        if (!await db.UserRoleAssociations.AnyAsync(x =>
                x.UserId == userId && x.RoleId == roleId && x.CenterId == centerId && x.IsActive))
        {
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
    }

    private static async Task SeedClinicAsync(AppDbContext db, Guid id, string code, string nameAr, bool isActive = true)
    {
        if (await db.Clinics.AnyAsync(c => c.ClinicId == id))
            return;

        db.Clinics.Add(new Clinic
        {
            ClinicId = id,
            Code = code,
            NameAr = nameAr,
            SortOrder = 1,
            IsActive = isActive
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCenterClinicAsync(
        AppDbContext db,
        Guid centerId,
        Guid clinicId,
        bool isActive = true)
    {
        if (await db.CenterClinics.AnyAsync(x => x.CenterId == centerId && x.ClinicId == clinicId))
            return;

        db.CenterClinics.Add(new CenterClinic
        {
            CenterClinicId = Guid.NewGuid(),
            CenterId = centerId,
            ClinicId = clinicId,
            IsActive = isActive,
            ActivatedAt = DateTimeOffset.UtcNow,
            DeactivatedAt = isActive ? null : DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
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

    private static async Task SeedUserAsync(AppDbContext db, string id, string userName, bool isActive = true)
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
            FullName = userName,
            IsActive = isActive,
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
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
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
