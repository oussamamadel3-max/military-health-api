using Application.Common.Permissions;
using Application.Common.Security;
using Application.Services.AuthServices;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.IntegrationTests.Infrastructure;

/// <summary>بذرة HTTP لعزل صلاحيات إسناد الأطباء بين المراكز.</summary>
public static class UserClinicAssignmentHttpSeed
{
    public const string ClinicTestPassword = "ClinicT3st!Pass1";
    public const string LocalCrossUserName = "it.clinic.cross";
    public const string CentralSuperUserName = "it.clinic.super";
    public const string CentralAuditorUserName = "it.clinic.auditor";
    public const string DoctorUserName = "it.clinic.doctor";
    public const string Doctor2UserName = "it.clinic.doctor2";
    public const string RevokedCenterUserName = "it.clinic.revcenter";
    public const string MixedAuditorManagerUserName = "it.clinic.mixed";
    public const string DualCenterManagerUserName = "it.clinic.dualmgr";

    public static readonly Guid CenterA = Guid.Parse("f1111111-1111-1111-1111-111111111111");
    public static readonly Guid CenterB = Guid.Parse("f2222222-2222-2222-2222-222222222222");
    public static readonly Guid ClinicEyes = Guid.Parse("f3333333-3333-3333-3333-333333333333");

    public static string DoctorUserId { get; private set; } = string.Empty;
    public static string Doctor2UserId { get; private set; } = string.Empty;
    public static Guid SeededAssignmentCenterA { get; private set; }
    public static Guid SeededAssignmentCenterB { get; private set; }

    public static async Task EnsureFixturesAsync(IServiceProvider root)
    {
        await using var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var users = sp.GetRequiredService<UserManager<User>>();

        if (await users.FindByNameAsync(LocalCrossUserName) is not null)
        {
            var existingManagerRoleId = await db.Roles
                .Where(r => r.Name == "CenterManager")
                .Select(r => r.Id)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrEmpty(existingManagerRoleId))
                await EnsureDualCenterManagerUserAsync(db, users, existingManagerRoleId);
            var existingDoctorRoleId = await db.Roles
                .Where(r => r.Name == UserClinicAssignmentService.ClinicDoctorRoleName)
                .Select(r => r.Id)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrEmpty(existingDoctorRoleId))
                await EnsureDoctor2Async(db, users, existingDoctorRoleId);
            var existingSuper = await users.FindByNameAsync(CentralSuperUserName);
            if (existingSuper is not null)
                EnsureUserCenter(db, existingSuper.Id, CenterB);
            var existingPlatformAdmin = await users.FindByNameAsync(AuthTestSeed.AdminUserName);
            if (existingPlatformAdmin is not null)
                EnsureUserCenter(db, existingPlatformAdmin.Id, CenterB);
            await db.SaveChangesAsync();
            return;
        }

        await EnsureReferenceCatalogAsync(db);

        var manageClinics = await EnsurePermissionAsync(db, MilitaryHealthPermissions.UserClinics.Manage, "IT clinic manage");
        var assignRoles = await EnsurePermissionAsync(db, SystemPermissions.UserManagement.AssignRoles, "IT assign roles");

        var centerManagerRoleId = await EnsureRoleAsync(db, "CenterManager");
        LinkRolePermission(db, centerManagerRoleId, manageClinics.Id);
        LinkRolePermission(db, centerManagerRoleId, assignRoles.Id);

        var superRoleId = await EnsureRoleAsync(db, "SuperAdmin");
        foreach (var name in MilitaryHealthSeedCatalog.GrantsByRoleName()["SuperAdmin"])
        {
            var p = await EnsurePermissionAsync(db, name, name);
            LinkRolePermission(db, superRoleId, p.Id);
        }

        var auditorRoleId = await EnsureRoleAsync(db, "Auditor");
        foreach (var name in MilitaryHealthSeedCatalog.GrantsByRoleName()["Auditor"])
        {
            var p = await EnsurePermissionAsync(db, name, name);
            LinkRolePermission(db, auditorRoleId, p.Id);
        }

        var doctorRoleId = await EnsureRoleAsync(db, UserClinicAssignmentService.ClinicDoctorRoleName);

        var cross = await EnsureUserAsync(users, LocalCrossUserName, "it.clinic.cross@it.local", ClinicTestPassword);
        var super = await EnsureUserAsync(users, CentralSuperUserName, "it.clinic.super@it.local", ClinicTestPassword);
        var auditor = await EnsureUserAsync(users, CentralAuditorUserName, "it.clinic.auditor@it.local", ClinicTestPassword);
        var doctor = await EnsureUserAsync(users, DoctorUserName, "it.clinic.doctor@it.local", ClinicTestPassword);
        var doctor2 = await EnsureUserAsync(users, Doctor2UserName, "it.clinic.doctor2@it.local", ClinicTestPassword);
        var revoked = await EnsureUserAsync(users, RevokedCenterUserName, "it.clinic.revcenter@it.local", ClinicTestPassword);
        var mixed = await EnsureUserAsync(users, MixedAuditorManagerUserName, "it.clinic.mixed@it.local", ClinicTestPassword);
        var dualMgr = await EnsureUserAsync(users, DualCenterManagerUserName, "it.clinic.dualmgr@it.local", ClinicTestPassword);
        DoctorUserId = doctor.Id;
        Doctor2UserId = doctor2.Id;

        EnsureUserCenter(db, cross.Id, CenterA);
        EnsureUserCenter(db, cross.Id, CenterB);
        LinkRole(db, cross.Id, centerManagerRoleId, CenterA);

        EnsureUserCenter(db, mixed.Id, CenterA);
        EnsureUserCenter(db, mixed.Id, CenterB);
        LinkRole(db, mixed.Id, centerManagerRoleId, CenterA);
        LinkRole(db, mixed.Id, auditorRoleId, null);

        EnsureUserCenter(db, dualMgr.Id, CenterA);
        EnsureUserCenter(db, dualMgr.Id, CenterB);
        LinkRole(db, dualMgr.Id, centerManagerRoleId, CenterA);
        LinkRole(db, dualMgr.Id, centerManagerRoleId, CenterB);

        LinkRole(db, super.Id, superRoleId, null);
        EnsureUserCenter(db, super.Id, CenterB);
        var platformAdmin = await users.FindByNameAsync(AuthTestSeed.AdminUserName);
        if (platformAdmin is not null)
            EnsureUserCenter(db, platformAdmin.Id, CenterB);
        LinkRole(db, auditor.Id, auditorRoleId, null);

        EnsureUserCenter(db, doctor.Id, CenterA);
        EnsureUserCenter(db, doctor.Id, CenterB);
        LinkRole(db, doctor.Id, doctorRoleId, CenterA);
        LinkRole(db, doctor.Id, doctorRoleId, CenterB);

        EnsureUserCenter(db, doctor2.Id, CenterB);
        LinkRole(db, doctor2.Id, doctorRoleId, CenterB);

        EnsureUserCenter(db, revoked.Id, CenterA);
        LinkRole(db, revoked.Id, centerManagerRoleId, CenterA);

        SeededAssignmentCenterA = Guid.NewGuid();
        SeededAssignmentCenterB = Guid.NewGuid();
        db.UserClinicAssignments.Add(new UserClinicAssignment
        {
            AssignmentId = SeededAssignmentCenterA,
            UserId = doctor.Id,
            CenterId = CenterA,
            ClinicId = ClinicEyes,
            SpecialtyTitle = "العيون",
            IsActive = true,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = super.Id
        });
        db.UserClinicAssignments.Add(new UserClinicAssignment
        {
            AssignmentId = SeededAssignmentCenterB,
            UserId = doctor.Id,
            CenterId = CenterB,
            ClinicId = ClinicEyes,
            SpecialtyTitle = "العيون",
            IsActive = true,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = super.Id
        });

        await db.SaveChangesAsync();
    }

    private static async Task EnsureReferenceCatalogAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        if (!await db.Users.AnyAsync(u => u.Id == "actor-1"))
        {
            db.Users.Add(new User
            {
                Id = "actor-1",
                UserName = "actor",
                NormalizedUserName = "ACTOR",
                Email = "actor@test.local",
                NormalizedEmail = "ACTOR@TEST.LOCAL",
                SecurityStamp = Guid.NewGuid().ToString(),
                IsActive = true
            });
        }

        foreach (var (id, code, name) in new[]
                 {
                     (CenterA, "CLA", "مركز أ عيادات"),
                     (CenterB, "CLB", "مركز ب عيادات")
                 })
        {
            if (await db.MedicalCenters.AnyAsync(c => c.CenterId == id))
                continue;
            db.MedicalCenters.Add(new MedicalCenter
            {
                CenterId = id,
                Code = code,
                NameAr = name,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (!await db.Clinics.AnyAsync(c => c.ClinicId == ClinicEyes))
        {
            db.Clinics.Add(new Clinic
            {
                ClinicId = ClinicEyes,
                Code = "EYE",
                NameAr = "العيون",
                SortOrder = 1,
                CountsForFinal = true,
                IsActive = true
            });
        }

        foreach (var centerId in new[] { CenterA, CenterB })
        {
            if (await db.CenterClinics.AnyAsync(x => x.CenterId == centerId && x.ClinicId == ClinicEyes))
                continue;
            db.CenterClinics.Add(new CenterClinic
            {
                CenterClinicId = Guid.NewGuid(),
                CenterId = centerId,
                ClinicId = ClinicEyes,
                IsActive = true,
                ActivatedAt = now
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<Permission> EnsurePermissionAsync(AppDbContext db, string name, string description)
    {
        var existing = await db.Permissions.FirstOrDefaultAsync(p => p.Name == name);
        if (existing is not null)
            return existing;

        var permission = new Permission
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            IsActive = true
        };
        db.Permissions.Add(permission);
        await db.SaveChangesAsync();
        return permission;
    }

    private static async Task<string> EnsureRoleAsync(AppDbContext db, string name)
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

    private static void LinkRolePermission(AppDbContext db, string roleId, Guid permissionId)
    {
        if (db.RolePermissions.Any(rp => rp.RoleId == roleId && rp.PermissionId == permissionId && rp.IsActive))
            return;
        db.RolePermissions.Add(new RolePermission
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            PermissionId = permissionId,
            IsActive = true,
            GrantedByUserId = "actor-1",
            GrantedAt = DateTimeOffset.UtcNow
        });
    }

    private static void EnsureUserCenter(AppDbContext db, string userId, Guid centerId, bool isActive = true)
    {
        var row = db.UserCenters.FirstOrDefault(uc => uc.UserId == userId && uc.CenterId == centerId);
        if (row is null)
        {
            db.UserCenters.Add(new UserCenter
            {
                UserId = userId,
                CenterId = centerId,
                IsActive = isActive,
                AssignedAt = DateTimeOffset.UtcNow,
                AssignedByUserId = userId
            });
            return;
        }

        row.IsActive = isActive;
        if (!isActive)
            row.RevokedAt = DateTimeOffset.UtcNow;
    }

    private static void LinkRole(AppDbContext db, string userId, string roleId, Guid? centerId)
    {
        if (db.UserRoleAssociations.Any(ura =>
                ura.UserId == userId && ura.RoleId == roleId && ura.CenterId == centerId && ura.IsActive))
            return;
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = roleId,
            CenterId = centerId,
            IsActive = true,
            AssignedByUserId = "actor-1",
            AssignedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task EnsureDoctor2Async(
        AppDbContext db,
        UserManager<User> users,
        string doctorRoleId)
    {
        if (await users.FindByNameAsync(Doctor2UserName) is { } existingDoctor2)
        {
            Doctor2UserId = existingDoctor2.Id;
            return;
        }

        var doctor2 = await EnsureUserAsync(users, Doctor2UserName, "it.clinic.doctor2@it.local", ClinicTestPassword);
        Doctor2UserId = doctor2.Id;
        EnsureUserCenter(db, doctor2.Id, CenterB);
        LinkRole(db, doctor2.Id, doctorRoleId, CenterB);
        await db.SaveChangesAsync();
    }

    private static async Task EnsureDualCenterManagerUserAsync(
        AppDbContext db,
        UserManager<User> users,
        string centerManagerRoleId)
    {
        if (await users.FindByNameAsync(DualCenterManagerUserName) is not null)
            return;

        var dual = await EnsureUserAsync(users, DualCenterManagerUserName, "it.clinic.dualmgr@it.local", ClinicTestPassword);
        EnsureUserCenter(db, dual.Id, CenterA);
        EnsureUserCenter(db, dual.Id, CenterB);
        LinkRole(db, dual.Id, centerManagerRoleId, CenterA);
        LinkRole(db, dual.Id, centerManagerRoleId, CenterB);
        await db.SaveChangesAsync();
    }

    private static async Task<User> EnsureUserAsync(
        UserManager<User> users,
        string userName,
        string email,
        string password)
    {
        var existing = await users.FindByNameAsync(userName);
        if (existing is not null)
            return existing;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            IsActive = true,
            FullName = userName,
            CreationDate = DateTime.UtcNow,
            SecurityStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }
}
