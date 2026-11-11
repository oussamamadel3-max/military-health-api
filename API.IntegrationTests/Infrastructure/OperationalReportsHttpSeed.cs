using Application.Common.Permissions;
using Domain.Entities.Auth;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.IntegrationTests.Infrastructure;

/// <summary>بذرة HTTP لـ FR-068 ضمن مصنع الاختبارات المعزول (SQLite).</summary>
public static class OperationalReportsHttpSeed
{
    public const string IndicatorsUserName = "it.rpt.indicators";
    public const string FullAccessUserName = "it.rpt.full";
    public const string ReportsTestPassword = "RptT3st!Pass1";

    public static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public static async Task EnsureDoctorLinkageHttpFixturesAsync(IServiceProvider root)
    {
        await using var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var users = sp.GetRequiredService<UserManager<User>>();

        if (await users.FindByNameAsync(IndicatorsUserName) is not null)
            return;

        await EnsureReferenceCatalogAsync(db);
        var reportsLocal = await EnsurePermissionAsync(db, MilitaryHealthPermissions.Reports.Local, "IT reports local");
        var membersView = await EnsurePermissionAsync(db, MilitaryHealthPermissions.Members.View, "IT members view");

        var indicators = await EnsureUserAsync(users, IndicatorsUserName, "it.rpt.indicators@it.local", ReportsTestPassword);
        var full = await EnsureUserAsync(users, FullAccessUserName, "it.rpt.full@it.local", ReportsTestPassword);

        foreach (var user in new[] { indicators, full })
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
        }

        GrantDirect(db, indicators.Id, reportsLocal.Id, CenterB);
        GrantDirect(db, full.Id, reportsLocal.Id, CenterB);
        GrantDirect(db, full.Id, membersView.Id, CenterB);

        await SeedLinkageExamAsync(db);
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

        if (!await db.MedicalCenters.AnyAsync(c => c.CenterId == CenterA))
        {
            db.MedicalCenters.Add(new MedicalCenter
            {
                CenterId = CenterA,
                Code = "A",
                NameAr = "مركز أ",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
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

        if (!await db.CenterClinics.AnyAsync(x => x.CenterId == CenterB && x.ClinicId == ClinicEyes))
        {
            db.CenterClinics.Add(new CenterClinic
            {
                CenterClinicId = Guid.NewGuid(),
                CenterId = CenterB,
                ClinicId = ClinicEyes,
                IsActive = true,
                ActivatedAt = now
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

        if (!await db.ResultStatuses.AnyAsync())
        {
            db.ResultStatuses.AddRange(
                new ResultStatus { StatusId = 1, Code = "ACCEPTED", NameAr = "مقبول", IsActive = true },
                new ResultStatus { StatusId = 2, Code = "REJECTED", NameAr = "مرفوض", IsActive = true },
                new ResultStatus { StatusId = 3, Code = "DEFERRED", NameAr = "مؤجل", IsActive = true });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedLinkageExamAsync(AppDbContext db)
    {
        const string recruitment = "HTTP-FR068-LNK";
        if (await db.Members.AnyAsync(m => m.RecruitmentNumber == recruitment))
            return;

        var memberId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Members.Add(new Member
        {
            MemberId = memberId,
            FileNumber = 88001,
            QueueNumber = 1,
            QueueDate = DateOnly.FromDateTime(now.UtcDateTime),
            FullName = "منتسب تقرير HTTP",
            MotherName = "أم الاختبار",
            Occupation = "طالب",
            RecruitmentNumber = recruitment,
            CurrentMedicalCenterId = CenterB,
            QueueMedicalCenterId = CenterB,
            MaritalStatusId = 1,
            BloodTypeId = 1,
            RecruitmentCenterId = RecruitmentCenterId,
            BirthDate = new DateOnly(2000, 1, 1),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = "actor-1",
            UpdatedByUserId = "actor-1"
        });

        var examId = Guid.NewGuid();
        db.MedicalExams.Add(new MedicalExam
        {
            ExamId = examId,
            MemberId = memberId,
            CenterId = CenterB,
            ClinicId = ClinicEyes,
            ExaminerUserId = "actor-1",
            ExaminedAt = now,
            ExamSequence = 1,
            IsCurrent = true,
            VersionNo = 1,
            UpdatedByUserId = "actor-1",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ClinicAssessments.Add(new ClinicAssessment
        {
            AssessmentId = Guid.NewGuid(),
            ExamId = examId,
            MemberId = memberId,
            CenterId = CenterB,
            ClinicId = ClinicEyes,
            ResultStatusId = 1,
            AssessedByUserId = "actor-1",
            AssessedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });
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
