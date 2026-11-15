using Application.Common.Permissions;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.IntegrationTests.Infrastructure;

/// <summary>بذرة HTTP لإنفاذ MotherName (Phase C) — قابلة للمراجعة، لا تُفعَّل في الإنتاج.</summary>
public static class FieldPermissionsEnforcementHttpSeed
{
    public const string ReaderUserName = "it.fp.reader";
    public const string WriterUserName = "it.fp.writer";
    public const string NoneUserName = "it.fp.none";
    public const string ReadWriteUserName = "it.fp.readwrite";
    public const string Password = "FpT3st!Pass1";

    public static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    public static readonly Guid SeededMemberId = Guid.Parse("f1f1f1f1-0001-4001-8001-000000000001");

    public const string SeededMotherName = "أم البذرة السرية";

    public static async Task EnsureAsync(IServiceProvider root)
    {
        await using var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var users = sp.GetRequiredService<UserManager<User>>();

        if (await users.FindByNameAsync(ReaderUserName) is not null)
            return;

        await EnsureCatalogAsync(db);

        var membersView = await EnsurePermissionAsync(db, MilitaryHealthPermissions.Members.View, "IT members view");
        var membersCreate = await EnsurePermissionAsync(db, MilitaryHealthPermissions.Members.Create, "IT members create");
        var membersUpdate = await EnsurePermissionAsync(db, MilitaryHealthPermissions.Members.Update, "IT members update");

        var reader = await EnsureUserAsync(users, ReaderUserName, "it.fp.reader@it.local");
        var writer = await EnsureUserAsync(users, WriterUserName, "it.fp.writer@it.local");
        var none = await EnsureUserAsync(users, NoneUserName, "it.fp.none@it.local");
        var readWrite = await EnsureUserAsync(users, ReadWriteUserName, "it.fp.readwrite@it.local");

        foreach (var user in new[] { reader, writer, none, readWrite })
        {
            if (!await db.UserCenters.AnyAsync(uc => uc.UserId == user.Id && uc.CenterId == CenterB))
            {
                db.UserCenters.Add(new UserCenter
                {
                    UserId = user.Id,
                    CenterId = CenterB,
                    IsActive = true,
                    AssignedAt = DateTimeOffset.UtcNow,
                    AssignedByUserId = user.Id
                });
            }

            GrantDirect(db, user.Id, membersView.Id, CenterB);
            GrantDirect(db, user.Id, membersCreate.Id, CenterB);
            GrantDirect(db, user.Id, membersUpdate.Id, CenterB);
        }

        AssignField(db, reader.Id, FieldAccessType.Read);
        AssignField(db, writer.Id, FieldAccessType.Write);
        AssignField(db, none.Id, FieldAccessType.None);
        AssignField(db, readWrite.Id, FieldAccessType.ReadWrite);

        if (!await db.Members.AnyAsync(m => m.MemberId == SeededMemberId))
        {
            var now = DateTimeOffset.UtcNow;
            db.Members.Add(new Member
            {
                MemberId = SeededMemberId,
                FileNumber = 99001,
                QueueNumber = 1,
                QueueDate = DateOnly.FromDateTime(now.UtcDateTime),
                FullName = "منتسب صلاحيات حقل HTTP",
                MotherName = SeededMotherName,
                Occupation = "طالب",
                RecruitmentNumber = "HTTP-FP-C-001",
                CurrentMedicalCenterId = CenterB,
                QueueMedicalCenterId = CenterB,
                MaritalStatusId = 1,
                BloodTypeId = 1,
                RecruitmentCenterId = RecruitmentCenterId,
                BirthDate = new DateOnly(2001, 2, 2),
                HeightCm = 170,
                WeightKg = 65,
                ExportStatus = "UNEXPORTED",
                DataRevision = 1,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedByUserId = "actor-1",
                UpdatedByUserId = "actor-1"
            });
        }

        await db.SaveChangesAsync();
    }

    private static void AssignField(AppDbContext db, string userId, FieldAccessType access)
    {
        var fp = new FieldPermission
        {
            Id = Guid.NewGuid(),
            EntityName = "Member",
            FieldName = "MotherName",
            AccessType = access,
            Description = $"IT Phase C {access}"
        };
        db.FieldPermissions.Add(fp);
        db.UserFieldPermissions.Add(new UserFieldPermission
        {
            Id = Guid.NewGuid(),
            FieldPermissionId = fp.Id,
            UserId = userId
        });
    }

    private static async Task EnsureCatalogAsync(AppDbContext db)
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

        if (!await db.MedicalCenters.AnyAsync(c => c.CenterId == CenterB))
        {
            db.MedicalCenters.Add(new MedicalCenter
            {
                CenterId = CenterB,
                Code = "B",
                NameAr = "مركز ب",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (!await db.MaritalStatuses.AnyAsync())
        {
            db.MaritalStatuses.Add(new MaritalStatus
            {
                MaritalStatusId = 1,
                Code = "S",
                NameAr = "أعزب",
                SortOrder = 1,
                IsSystem = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (!await db.BloodTypes.AnyAsync())
        {
            db.BloodTypes.Add(new BloodType
            {
                BloodTypeId = 1,
                Code = "O+",
                NameAr = "O+",
                SortOrder = 1,
                IsSystem = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (!await db.RecruitmentCenters.AnyAsync())
        {
            db.RecruitmentCenters.Add(new RecruitmentCenter
            {
                RecruitmentCenterId = RecruitmentCenterId,
                Code = "RC1",
                NameAr = "تجنيد 1",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (!await db.BmiCategories.AnyAsync())
        {
            db.BmiCategories.Add(new BmiCategory
            {
                CategoryId = 1,
                Code = "NORMAL",
                NameAr = "طبيعي",
                MinBmi = 18.5m,
                MaxBmi = 25m,
                SortOrder = 1,
                IsActive = true
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

    private static async Task<User> EnsureUserAsync(UserManager<User> users, string userName, string email)
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
        var result = await users.CreateAsync(user, Password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    private static void GrantDirect(AppDbContext db, string userId, Guid permissionId, Guid centerId)
    {
        if (db.UserPermissions.Any(up =>
                up.UserId == userId && up.PermissionId == permissionId && up.CenterId == centerId && up.IsActive))
            return;

        db.UserPermissions.Add(new UserPermission
        {
            UserId = userId,
            PermissionId = permissionId,
            CenterId = centerId,
            IsActive = true,
            GrantedByUserId = userId,
            GrantedAt = DateTimeOffset.UtcNow
        });
    }
}
