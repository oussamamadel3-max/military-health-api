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

public class CenterAssignmentListTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClinicInternal = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ClinicSurgery = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task View_guard_blocks_local_actor_from_foreign_center()
    {
        await using var db = CreateDb(CenterA, centralContext: false);
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedCenterAsync(db, CenterB, "CB", "مركز ب");
        await SeedUserAsync(db, "actor-1", "manager", "مدير");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedLocalManagerRoleAsync(db, "actor-1", CenterA);

        var centers = new MedicalCenterRepository(db);
        var access = new AccessContextRepository(db);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            UserManagementScopeGuard.EnsureCanViewCenterDataAsync(
                access,
                centers,
                "actor-1",
                CenterB,
                new TestCenter(CenterA, false),
                SystemPermissions.UserManagement.Read));
    }

    [Fact]
    public async Task List_roles_rejects_foreign_center_for_local_manager()
    {
        await using var db = CreateDb(CenterA, centralContext: false);
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedCenterAsync(db, CenterB, "CB", "مركز ب");
        await SeedUserAsync(db, "actor-1", "manager", "مدير");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedLocalManagerRoleAsync(db, "actor-1", CenterA);
        var service = CreateService(db, unrestrictedActor: false, actorId: "actor-1", centerId: CenterA);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ListCenterRoleAssignmentsAsync(new CenterRoleAssignmentListQuery
            {
                CenterId = CenterB,
                PageNumber = 1,
                PageSize = 10
            }));
    }

    [Fact]
    public async Task Active_doctor_with_two_clinics_returns_two_rows()
    {
        await using var db = CreateDb(CenterA, centralContext: false);
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "doc-1", "doctor", "د. أحمد");
        var doctorRoleId = await SeedRoleAsync(db, "ClinicDoctor");
        await SeedClinicAsync(db, ClinicInternal, "INT", "الباطنة", 1);
        await SeedClinicAsync(db, ClinicSurgery, "SUR", "الجراحة", 2);
        await SeedCenterClinicAsync(db, CenterA, ClinicInternal);
        await SeedCenterClinicAsync(db, CenterA, ClinicSurgery);

        var uraId = Guid.NewGuid();
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = uraId,
            UserId = "doc-1",
            RoleId = doctorRoleId,
            CenterId = CenterA,
            IsActive = true,
            AssignedByUserId = "actor-1",
            AssignedAt = DateTimeOffset.UtcNow
        });
        await SeedDoctorAssignmentAsync(db, "doc-1", CenterA, ClinicInternal);
        await SeedDoctorAssignmentAsync(db, "doc-1", CenterA, ClinicSurgery);
        await db.SaveChangesAsync();

        var service = CreateService(db, unrestrictedActor: true, centerId: CenterA);

        var page = await service.ListCenterRoleAssignmentsAsync(new CenterRoleAssignmentListQuery
        {
            CenterId = CenterA,
            PageNumber = 1,
            PageSize = 10,
            IsActive = true
        });

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, i => Assert.Equal(uraId, i.RoleAssignmentId));
        Assert.Contains(page.Items, i => i.ClinicId == ClinicInternal);
        Assert.Contains(page.Items, i => i.ClinicId == ClinicSurgery);
        Assert.Equal(2, page.Items.Select(i => i.RowKey).Distinct().Count());
    }

    [Fact]
    public async Task Active_doctor_without_clinic_returns_single_row_with_null_clinic()
    {
        await using var db = CreateDb(CenterA, centralContext: false);
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "doc-1", "doctor", "د. بلا عيادة");
        var doctorRoleId = await SeedRoleAsync(db, "ClinicDoctor");
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = "doc-1",
            RoleId = doctorRoleId,
            CenterId = CenterA,
            IsActive = true,
            AssignedByUserId = "actor-1",
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, unrestrictedActor: true, centerId: CenterA);
        var page = await service.ListCenterRoleAssignmentsAsync(new CenterRoleAssignmentListQuery
        {
            CenterId = CenterA,
            PageNumber = 1,
            PageSize = 10
        });

        Assert.Equal(1, page.TotalCount);
        var item = Assert.Single(page.Items);
        Assert.Null(item.ClinicId);
    }

    [Fact]
    public async Task Revoked_doctor_role_does_not_explode_active_clinics()
    {
        await using var db = CreateDb(CenterA, centralContext: false);
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "doc-1", "doctor", "د. ملغى");
        var doctorRoleId = await SeedRoleAsync(db, "ClinicDoctor");
        await SeedClinicAsync(db, ClinicInternal, "INT", "الباطنة", 1);
        await SeedCenterClinicAsync(db, CenterA, ClinicInternal);
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = "doc-1",
            RoleId = doctorRoleId,
            CenterId = CenterA,
            IsActive = false,
            AssignedByUserId = "actor-1",
            AssignedAt = DateTimeOffset.UtcNow.AddDays(-10),
            RevokedAt = DateTimeOffset.UtcNow,
            RevokedByUserId = "actor-1",
            RevokeReason = "سحب"
        });
        await SeedDoctorAssignmentAsync(db, "doc-1", CenterA, ClinicInternal);
        await db.SaveChangesAsync();

        var service = CreateService(db, unrestrictedActor: true, centerId: CenterA);
        var page = await service.ListCenterRoleAssignmentsAsync(new CenterRoleAssignmentListQuery
        {
            CenterId = CenterA,
            PageNumber = 1,
            PageSize = 10,
            IsActive = false
        });

        Assert.Equal(1, page.TotalCount);
        Assert.Null(Assert.Single(page.Items).ClinicId);
    }

    [Fact]
    public async Task Revoked_role_visible_without_active_user_center()
    {
        await using var db = CreateDb(CenterA, centralContext: false);
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "nurse-1", "nurse", "ممرض");
        var roleId = await SeedRoleAsync(db, "ReceptionClerk");
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = "nurse-1",
            RoleId = roleId,
            CenterId = CenterA,
            IsActive = false,
            AssignedByUserId = "actor-1",
            AssignedAt = DateTimeOffset.UtcNow.AddDays(-30),
            RevokedAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedByUserId = "actor-1",
            RevokeReason = "سحب"
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, unrestrictedActor: true, centerId: CenterA);
        var page = await service.ListCenterRoleAssignmentsAsync(new CenterRoleAssignmentListQuery
        {
            CenterId = CenterA,
            PageNumber = 1,
            PageSize = 10,
            IsActive = false
        });

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("nurse-1", page.Items.First().UserId);
    }

    [Fact]
    public async Task Pagination_returns_stable_pages_without_overlap()
    {
        await using var db = CreateDb(CenterA, centralContext: false);
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        var clerkRole = await SeedRoleAsync(db, "ReceptionClerk");
        for (var i = 0; i < 5; i++)
        {
            var id = $"user-{i}";
            await SeedUserAsync(db, id, id, $"مستخدم {i}");
            db.UserRoleAssociations.Add(new UserRoleAssociation
            {
                Id = Guid.NewGuid(),
                UserId = id,
                RoleId = clerkRole,
                CenterId = CenterA,
                IsActive = true,
                AssignedByUserId = "actor-1",
                AssignedAt = DateTimeOffset.UtcNow.AddMinutes(-i)
            });
        }
        await db.SaveChangesAsync();

        var service = CreateService(db, unrestrictedActor: true, centerId: CenterA);
        var page1 = await service.ListCenterRoleAssignmentsAsync(new CenterRoleAssignmentListQuery
        {
            CenterId = CenterA,
            PageNumber = 1,
            PageSize = 2
        });
        var page2 = await service.ListCenterRoleAssignmentsAsync(new CenterRoleAssignmentListQuery
        {
            CenterId = CenterA,
            PageNumber = 2,
            PageSize = 2
        });

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Empty(page1.Items.Select(i => i.RowKey).Intersect(page2.Items.Select(i => i.RowKey)));
    }

    [Fact]
    public async Task Direct_permissions_list_returns_only_user_permission_rows()
    {
        await using var db = CreateDb(CenterA, centralContext: false);
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "nurse-1", "nurse", "ممرض");
        var roleId = await SeedRoleAsync(db, "ReceptionClerk");
        await SeedRolePermissionAsync(db, roleId, "Members.CreateUpdate");
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = "nurse-1",
            RoleId = roleId,
            CenterId = CenterA,
            IsActive = true,
            AssignedByUserId = "actor-1",
            AssignedAt = DateTimeOffset.UtcNow
        });

        var permission = await db.Permissions.FirstAsync(p => p.Name == "Members.CreateUpdate");
        db.UserPermissions.Add(new UserPermission
        {
            Id = Guid.NewGuid(),
            UserId = "nurse-1",
            PermissionId = permission.Id,
            CenterId = CenterA,
            IsActive = true,
            GrantedByUserId = "actor-1",
            GrantedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, unrestrictedActor: true, centerId: CenterA);
        var page = await service.ListCenterDirectPermissionsAsync(new CenterDirectPermissionListQuery
        {
            CenterId = CenterA,
            PageNumber = 1,
            PageSize = 10
        });

        Assert.Equal(1, page.TotalCount);
        var item = Assert.Single(page.Items);
        Assert.Equal("Members.CreateUpdate", item.PermissionName);
        Assert.Equal("nurse-1", item.UserId);
    }

    [Fact]
    public async Task Get_assignments_includes_assigned_by_full_name()
    {
        await using var db = CreateDb(CenterA, centralContext: false);
        await SeedCenterAsync(db, CenterA, "CA", "مركز أ");
        await SeedUserAsync(db, "manager-1", "mgr", "مدير المركز");
        await SeedUserAsync(db, "nurse-1", "nurse", "ممرض");
        await SeedUserCenterAsync(db, "nurse-1", CenterA);
        var roleId = await SeedRoleAsync(db, "ReceptionClerk");
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = "nurse-1",
            RoleId = roleId,
            CenterId = CenterA,
            IsActive = true,
            AssignedByUserId = "manager-1",
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, unrestrictedActor: true, centerId: CenterA);
        var dto = await service.GetAssignmentsAsync("nurse-1");
        var role = Assert.Single(dto.Roles, r => r.CenterId == CenterA);
        Assert.Equal("مدير المركز", role.AssignedByFullName);
        Assert.NotEqual(default, role.LastChangedAt);
    }

    private static UserAssignmentService CreateService(
        AppDbContext db,
        bool unrestrictedActor,
        string actorId = "actor-1",
        Guid? centerId = null)
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

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureAll(It.IsAny<string>()));
        gate.Setup(g => g.GetPermissions()).Returns(new[] { SystemPermissions.UserManagement.Read });

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
            current,
            new TestCenter(centerId, unrestrictedActor && centerId is null),
            Mock.Of<IAuditLogRepository>());
    }

    private static AppDbContext CreateDb(Guid? centerId, bool centralContext)
    {
        var user = new TestCurrentUser();
        user.SetUser("actor-1", "admin", "127.0.0.1");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, user, new TestCenter(centerId, centralContext), new AuditInterceptor(user));
    }

    private static async Task SeedCentralActorAsync(AppDbContext db, string actorId)
    {
        await SeedUserAsync(db, actorId, "admin", "إداري");
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
        foreach (var permissionName in new[] { SystemPermissions.UserManagement.Read }
            .Concat(SystemPermissions.UserManagementWrite)
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

    private static async Task SeedUserAsync(AppDbContext db, string id, string userName, string? fullName = null)
    {
        if (await db.Users.AnyAsync(u => u.Id == id))
            return;

        db.Users.Add(new User
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            FullName = fullName,
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            CreationDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCenterAsync(AppDbContext db, Guid id, string code, string nameAr)
    {
        if (await db.MedicalCenters.AnyAsync(c => c.CenterId == id))
            return;

        var now = DateTimeOffset.UtcNow;
        db.MedicalCenters.Add(new MedicalCenter
        {
            CenterId = id,
            Code = code,
            NameAr = nameAr,
            IsActive = true,
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

        if (!await db.RolePermissions.AnyAsync(rp =>
                rp.RoleId == roleId && rp.PermissionId == permission.Id && rp.IsActive))
        {
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
    }

    private static async Task SeedClinicAsync(AppDbContext db, Guid clinicId, string code, string nameAr, int sort)
    {
        if (await db.Clinics.AnyAsync(c => c.ClinicId == clinicId))
            return;

        db.Clinics.Add(new Clinic
        {
            ClinicId = clinicId,
            Code = code,
            NameAr = nameAr,
            SortOrder = (short)sort,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCenterClinicAsync(AppDbContext db, Guid centerId, Guid clinicId)
    {
        if (await db.CenterClinics.AnyAsync(x => x.CenterId == centerId && x.ClinicId == clinicId))
            return;

        db.CenterClinics.Add(new CenterClinic
        {
            CenterClinicId = Guid.NewGuid(),
            CenterId = centerId,
            ClinicId = clinicId,
            IsActive = true,
            ActivatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedDoctorAssignmentAsync(AppDbContext db, string userId, Guid centerId, Guid clinicId)
    {
        if (await db.UserClinicAssignments.AnyAsync(a =>
                a.UserId == userId && a.CenterId == centerId && a.ClinicId == clinicId && a.IsActive))
            return;

        db.UserClinicAssignments.Add(new UserClinicAssignment
        {
            AssignmentId = Guid.NewGuid(),
            UserId = userId,
            CenterId = centerId,
            ClinicId = clinicId,
            IsActive = true,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = "actor-1"
        });
        await db.SaveChangesAsync();
    }

    private static Mock<UserManager<User>> CreateUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(store.Object, null, null, null, null, null, null, null, null);
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public TestUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Mock.Of<IDbContextTransaction>());
    }
}
