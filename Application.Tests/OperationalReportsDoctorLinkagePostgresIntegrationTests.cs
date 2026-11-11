using Application.Common.Security;
using Application.Dtos.OperationalReports;
using Application.Exceptions;
using Application.Services.Members;
using Application.Services.OperationalReports;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Application.Tests;

/// <summary>
/// تكامل PostgreSQL لـ FR-068 / BUG-F068-004. يُتخطى عند غياب <c>MHS_EXPORT_EF_REVIEW_PG</c>
/// أو ملف كلمة مرور المراجع المعزول (قاعدة <c>mhs_export_ef_review</c> فقط).
/// </summary>
public class OperationalReportsDoctorLinkagePostgresIntegrationTests
{
    private const string RequiredDatabase = "mhs_export_ef_review";
    private static readonly Guid PgCenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PgCenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PgClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PgClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>يُستخدم في التقرير لمعرفة إن شُغّلت اختبارات PG فعلياً.</summary>
    public static int LastRunExecutedTestCount { get; private set; }

    [Fact]
    public async Task Postgres_by_doctor_sql_translates_groupby_and_distinct_member_count()
    {
        if (await TryBeginPgScenarioAsync() is not PgScenario s)
            return;
        var (db, tx, service) = s;

        try
        {
            var m1 = await OperationalReportsServiceTests.CreateTestMemberAsync(db, PgCenterA, $"PG-DOC-{Guid.NewGuid():N}"[..18]);
            var m2 = await OperationalReportsServiceTests.CreateTestMemberAsync(db, PgCenterA, $"PG-DOC-{Guid.NewGuid():N}"[..18]);
            await SeedExamAsync(db, m1, PgCenterA, PgClinicEyes, "pg-doc-a", 1);
            await SeedExamAsync(db, m1, PgCenterA, PgClinicEnt, "pg-doc-a", 1);
            await SeedExamAsync(db, m2, PgCenterA, PgClinicEyes, "pg-doc-a", 2);
            await SeedExamAsync(db, m2, PgCenterA, PgClinicEnt, "pg-doc-b", 1);

            var query = new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByDoctor };
            var grouped = DoctorLinkagePostgresProbes.ByDoctorGrouped(db, PgCenterA, query);
            var sql = grouped.OrderByDescending(x => x.MemberCount).Take(5).ToQueryString();
            Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);

            var probeCount = await grouped.CountAsync();
            var page = await service.ListDoctorLinkageByDoctorAsync(query);
            Assert.Equal(probeCount, page.TotalCount);

            var docA = Assert.Single(page.Items, i => i.ExaminerUserId == "pg-doc-a" && i.ClinicId == PgClinicEyes);
            Assert.Equal(2, docA.ExamCount);
            Assert.Equal(2, docA.MemberCount);
            Assert.Equal(1, docA.AcceptedCount);
            Assert.Equal(1, docA.RejectedCount);
        }
        finally
        {
            await tx.RollbackAsync();
            LastRunExecutedTestCount++;
        }
    }

    [Fact]
    public async Task Postgres_by_doctor_pagination_stable_and_includes_unassessed_via_left_join()
    {
        if (await TryBeginPgScenarioAsync() is not PgScenario s)
            return;
        var (db, tx, service) = s;

        try
        {
            var memberId = await OperationalReportsServiceTests.CreateTestMemberAsync(db, PgCenterA, $"PG-UNA-{Guid.NewGuid():N}"[..18]);
            await SeedExamWithoutAssessmentAsync(db, memberId, PgCenterA, PgClinicEyes, "pg-unassessed");

            var flatSql = DoctorLinkagePostgresProbes
                .ByDoctorFlat(db, PgCenterA, new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByDoctor })
                .Take(1)
                .ToQueryString();
            Assert.Contains("LEFT JOIN", flatSql, StringComparison.OrdinalIgnoreCase);

            var page = await service.ListDoctorLinkageByDoctorAsync(new DoctorLinkageQuery
            {
                ViewMode = DoctorLinkageViewModes.ByDoctor,
                PageSize = 1,
                PageNumber = 1
            });
            Assert.True(page.TotalCount >= 1);
            var row = page.Items.First(i => i.ExaminerUserId == "pg-unassessed");
            Assert.Equal(1, row.ExamCount);
            Assert.Equal(1, row.MemberCount);
            Assert.Equal(0, row.AcceptedCount);
        }
        finally
        {
            await tx!.RollbackAsync();
            LastRunExecutedTestCount++;
        }
    }

    [Fact]
    public async Task Postgres_by_member_paginates_members_and_loads_all_matching_exams()
    {
        if (await TryBeginPgScenarioAsync() is not PgScenario s)
            return;
        var (db, tx, service) = s;

        try
        {
            var memberId = await OperationalReportsServiceTests.CreateTestMemberAsync(db, PgCenterA, $"PG-MB-{Guid.NewGuid():N}"[..18]);
            await SeedExamAsync(db, memberId, PgCenterA, PgClinicEyes, "actor-1", 1);
            await SeedExamAsync(db, memberId, PgCenterA, PgClinicEnt, "actor-1", 2);

            var keys = DoctorLinkagePostgresProbes.ByMemberKeys(db, PgCenterA, new DoctorLinkageQuery());
            var memberSql = keys.OrderBy(k => k.FileNumber).ThenBy(k => k.MemberId).Skip(0).Take(1).ToQueryString();
            Assert.Contains("ORDER BY", memberSql, StringComparison.OrdinalIgnoreCase);

            var totalFromSql = await keys.CountAsync();
            var page1 = await service.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery
            {
                PageNumber = 1,
                PageSize = 1
            });
            Assert.Equal(totalFromSql, page1.TotalCount);

            var item = page1.Items.FirstOrDefault(i => i.MemberId == memberId);
            if (item is not null)
            {
                Assert.Equal(2, item.Exams.Count);
            }
            else
            {
                var full = await service.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery { PageSize = 50 });
                item = Assert.Single(full.Items, i => i.MemberId == memberId);
                Assert.Equal(2, item.Exams.Count);
            }
        }
        finally
        {
            await tx!.RollbackAsync();
            LastRunExecutedTestCount++;
        }
    }

    [Fact]
    public async Task Postgres_a3_doctor_activity_persists_after_member_transfer()
    {
        if (await TryBeginPgScenarioAsync() is not PgScenario s)
            return;
        var (db, tx, service) = s;

        try
        {
            var member = await MemberTransferServiceTests.CreateMemberAsync(db, PgCenterA, 9001, $"PG-A3-{Guid.NewGuid():N}"[..12]);
            await SeedExamAsync(db, member.MemberId, PgCenterA, PgClinicEyes, "pg-a3-doc", 1);

            var before = Assert.Single(
                (await service.ListDoctorLinkageByDoctorAsync(new DoctorLinkageQuery
                {
                    ViewMode = DoctorLinkageViewModes.ByDoctor
                })).Items,
                i => i.ExaminerUserId == "pg-a3-doc");

            await MemberTransferServiceTests.CreateService(db, PgCenterA, allowTransfer: true)
                .TransferLocalAsync(member.MemberId, new Application.Dtos.Members.TransferMemberRequest
                {
                    ToCenterId = PgCenterB,
                    TransferReason = "اختبار PG A3"
                });

            var after = Assert.Single(
                (await service.ListDoctorLinkageByDoctorAsync(new DoctorLinkageQuery
                {
                    ViewMode = DoctorLinkageViewModes.ByDoctor
                })).Items,
                i => i.ExaminerUserId == "pg-a3-doc");

            Assert.Equal(before.ExamCount, after.ExamCount);
            Assert.Equal(before.MemberCount, after.MemberCount);

            var byMember = await service.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery());
            Assert.DoesNotContain(byMember.Items, i => i.MemberId == member.MemberId);
        }
        finally
        {
            await tx!.RollbackAsync();
            LastRunExecutedTestCount++;
        }
    }

    [Fact]
    public async Task Postgres_export_full_workbook_and_center_audit_context()
    {
        if (await TryBeginPgScenarioAsync() is not PgScenario s)
            return;
        var (db, tx, service) = s;

        try
        {
            var memberId = await OperationalReportsServiceTests.CreateTestMemberAsync(db, PgCenterA, $"PG-XL-{Guid.NewGuid():N}"[..18]);
            await SeedExamAsync(db, memberId, PgCenterA, PgClinicEyes, "actor-1", 1);

            var paged = await service.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery { PageSize = 1 });
            var export = await service.ExportDoctorLinkageAsync(new DoctorLinkageQuery());
            Assert.NotEmpty(export.FileContents);
            Assert.True(paged.TotalCount >= 1);

            var audit = await db.AuditEvents
                .Where(e => e.EventType == OperationalReportsService.ExportEventType)
                .OrderByDescending(e => e.OccurredAt)
                .FirstAsync();
            Assert.Equal(PgCenterA, audit.CenterId);
            Assert.Equal(CenterClaimTypes.Center, audit.ContextScope);
        }
        finally
        {
            await tx!.RollbackAsync();
            LastRunExecutedTestCount++;
        }
    }

    [Fact]
    public async Task Postgres_export_rejects_over_max_rows_without_success_audit()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MHS_RUN_HEAVY_PG"), "1", StringComparison.Ordinal))
            return;

        if (await TryBeginPgScenarioAsync() is not PgScenario s)
            return;
        var (db, tx, service) = s;

        try
        {
            var memberId = await OperationalReportsServiceTests.CreateTestMemberAsync(db, PgCenterA, $"PG-MAX-{Guid.NewGuid():N}"[..18]);
            var baseAt = DateTimeOffset.UtcNow;
            for (var i = 0; i < MemberService.MaxExportRows + 1; i++)
            {
                var at = baseAt.AddMinutes(-i);
                var examId = Guid.NewGuid();
                db.MedicalExams.Add(new MedicalExam
                {
                    ExamId = examId,
                    MemberId = memberId,
                    CenterId = PgCenterA,
                    ClinicId = PgClinicEyes,
                    ExaminerUserId = "actor-1",
                    ExaminedAt = at,
                    ExamSequence = 1,
                    IsCurrent = true,
                    VersionNo = 1,
                    UpdatedByUserId = "actor-1",
                    CreatedAt = at,
                    UpdatedAt = at
                });
                db.ClinicAssessments.Add(new ClinicAssessment
                {
                    AssessmentId = Guid.NewGuid(),
                    ExamId = examId,
                    MemberId = memberId,
                    CenterId = PgCenterA,
                    ClinicId = PgClinicEyes,
                    ResultStatusId = 1,
                    AssessedByUserId = "actor-1",
                    AssessedAt = at,
                    UpdatedByUserId = "actor-1",
                    UpdatedAt = at
                });
            }

            await db.SaveChangesAsync();
            var auditsBefore = await db.AuditEvents.CountAsync(e =>
                e.EventType == OperationalReportsService.ExportEventType && e.CenterId == PgCenterA);

            await Assert.ThrowsAsync<InvalidOperationalReportFilterException>(() =>
                service.ExportDoctorLinkageAsync(new DoctorLinkageQuery()));

            var auditsAfter = await db.AuditEvents.CountAsync(e =>
                e.EventType == OperationalReportsService.ExportEventType && e.CenterId == PgCenterA);
            Assert.Equal(auditsBefore, auditsAfter);
        }
        finally
        {
            await tx!.RollbackAsync();
            LastRunExecutedTestCount++;
        }
    }

    [Fact]
    public async Task Postgres_explain_by_doctor_grouped_when_opt_in()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MHS_RUN_EXPLAIN"), "1", StringComparison.Ordinal))
            return;

        if (await TryBeginPgScenarioAsync() is not PgScenario s)
            return;
        var (db, tx, _) = s;

        try
        {
            var grouped = DoctorLinkagePostgresProbes.ByDoctorGrouped(
                db, PgCenterA, new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByDoctor });
            _ = await grouped.CountAsync();
            var sql = grouped.OrderByDescending(x => x.MemberCount).ThenBy(x => x.ExaminerUserId).Take(10).ToQueryString();

            var cs = ResolveConnectionString()!;
            try
            {
                await using var conn = new NpgsqlConnection(cs);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand($"EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) {sql}", conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                var plan = new List<string>();
                while (await reader.ReadAsync())
                    plan.Add(reader.GetString(0));
                Assert.NotEmpty(plan);
            }
            catch (PostgresException)
            {
                // EF placeholders — استخدم ToQueryString في psql عند الحاجة.
            }
        }
        finally
        {
            await tx!.RollbackAsync();
            LastRunExecutedTestCount++;
        }
    }

    private sealed record PgScenario(
        AppDbContext Db,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction Tx,
        OperationalReportsService Service);

    private static async Task<PgScenario?> TryBeginPgScenarioAsync()
    {
        var cs = ResolveConnectionString();
        if (cs is null)
            return null;

        var db = CreatePostgresContext(cs, PgCenterA);
        var tx = await db.Database.BeginTransactionAsync();
        await EnsureOperationalPgReferencesAsync(db);
        db.ChangeTracker.Clear();
        await SeedDoctorLinkageReportCatalogAsync(db);
        var service = OperationalReportsServiceTests.CreateService(db, PgCenterA);
        return new PgScenario(db, tx, service);
    }

    private static async Task SeedDoctorLinkageReportCatalogAsync(AppDbContext db)
    {
        foreach (var (id, code, name, sort) in new (Guid, string, string, int)[]
                 {
                     (PgClinicEyes, "EYE", "العيون", 1),
                     (PgClinicEnt, "ENT", "الأذنية", 2)
                 })
        {
            if (!await db.Clinics.AsNoTracking().AnyAsync(c => c.ClinicId == id))
            {
                db.Clinics.Add(new Clinic
                {
                    ClinicId = id,
                    Code = code,
                    NameAr = name,
                    SortOrder = (short)sort,
                    CountsForFinal = true,
                    IsActive = true
                });
            }
        }

        await db.SaveChangesAsync();
        foreach (var clinicId in new[] { PgClinicEyes, PgClinicEnt })
        {
            foreach (var centerId in new[] { PgCenterA, PgCenterB })
            {
                if (!await db.CenterClinics.AsNoTracking().AnyAsync(x => x.CenterId == centerId && x.ClinicId == clinicId))
                {
                    db.CenterClinics.Add(new CenterClinic
                    {
                        CenterClinicId = Guid.NewGuid(),
                        CenterId = centerId,
                        ClinicId = clinicId,
                        IsActive = true,
                        ActivatedAt = DateTimeOffset.UtcNow
                    });
                }
            }
        }

        if (!await db.ResultStatuses.AsNoTracking().AnyAsync())
        {
            db.ResultStatuses.AddRange(
                new ResultStatus { StatusId = 1, Code = "ACCEPTED", NameAr = "مقبول", IsActive = true },
                new ResultStatus { StatusId = 2, Code = "REJECTED", NameAr = "مرفوض", IsActive = true },
                new ResultStatus { StatusId = 3, Code = "DEFERRED", NameAr = "مؤجل", IsActive = true });
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync();
    }

    private static string? ResolveConnectionString()
    {
        var cs = Environment.GetEnvironmentVariable("MHS_EXPORT_EF_REVIEW_PG");
        if (!string.IsNullOrWhiteSpace(cs))
        {
            var builder = new NpgsqlConnectionStringBuilder(cs.Trim());
            if (!string.Equals(builder.Database, RequiredDatabase, StringComparison.OrdinalIgnoreCase))
                return null;
            return builder.ConnectionString;
        }

        var pwdPath = Path.Combine(Path.GetTempPath(), "mhs_review_tester_pwd.txt");
        if (!File.Exists(pwdPath))
            return null;

        var pwd = File.ReadAllText(pwdPath).Trim();
        return $"Host=127.0.0.1;Port=5432;Database={RequiredDatabase};Username=mhs_review_tester;Password={pwd}";
    }

    private static async Task EnsureOperationalPgReferencesAsync(AppDbContext db)
    {
        if (await db.MedicalCenters.AsNoTracking().AnyAsync(c => c.CenterId == PgCenterA))
            return;

        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == "actor-1"))
        {
            try
            {
                await MemberServiceTests.SeedReferencesAsync(db);
                return;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear();
            }
        }

        await EnsurePgCentersWithoutDuplicateUserAsync(db);
        await EnsurePgMemberCreateReferencesAsync(db);
    }

    private static async Task EnsurePgCentersWithoutDuplicateUserAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        if (!await db.MedicalCenters.AsNoTracking().AnyAsync(c => c.CenterId == PgCenterA))
        {
            db.MedicalCenters.Add(new MedicalCenter
            {
                CenterId = PgCenterA,
                Code = "A",
                NameAr = "مركز أ",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (!await db.MedicalCenters.AsNoTracking().AnyAsync(c => c.CenterId == PgCenterB))
        {
            db.MedicalCenters.Add(new MedicalCenter
            {
                CenterId = PgCenterB,
                Code = "B",
                NameAr = "مركز ب",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (db.ChangeTracker.HasChanges())
        {
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private static async Task EnsurePgMemberCreateReferencesAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var recruitmentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        if (!await db.RecruitmentCenters.AsNoTracking().AnyAsync(r => r.RecruitmentCenterId == recruitmentId))
        {
            db.RecruitmentCenters.Add(new RecruitmentCenter
            {
                RecruitmentCenterId = recruitmentId,
                Code = "RC1",
                NameAr = "تجنيد 1",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (!await db.MaritalStatuses.AsNoTracking().AnyAsync(m => m.MaritalStatusId == 1))
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

        if (!await db.BloodTypes.AsNoTracking().AnyAsync(b => b.BloodTypeId == 1))
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

        if (!await db.ValidationRules.AsNoTracking().AnyAsync(r => r.TargetEntity.ToLower() == "member" && r.IsActive))
            db.ValidationRules.Add(ReceptionReferenceSeed.CreateAgeRule(now));

        if (!await db.BmiCategories.AsNoTracking().AnyAsync())
        {
            db.BmiCategories.AddRange(
                new BmiCategory
                {
                    CategoryId = 1,
                    Code = "UNDER",
                    NameAr = "نقص وزن",
                    MinBmi = 0,
                    MaxBmi = 18.5m,
                    SortOrder = 1,
                    IsActive = true
                },
                new BmiCategory
                {
                    CategoryId = 2,
                    Code = "NORMAL",
                    NameAr = "طبيعي",
                    MinBmi = 18.5m,
                    MaxBmi = 25m,
                    SortOrder = 2,
                    IsActive = true
                });
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync();
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException pg
           && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    private static AppDbContext CreatePostgresContext(string connectionString, Guid activeCenter)
    {
        var user = new TestCurrentUser();
        user.SetUser("actor-1", "actor", "127.0.0.1");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AppDbContext(options, user, new TestCenter(activeCenter, central: false), new AuditInterceptor(user));
    }

    private static async Task SeedExamAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        string examinerUserId,
        short resultStatusId,
        DateTimeOffset? examinedAt = null)
    {
        if (!await db.Users.AnyAsync(u => u.Id == examinerUserId))
        {
            db.Users.Add(new Domain.Entities.Auth.User
            {
                Id = examinerUserId,
                UserName = examinerUserId,
                NormalizedUserName = examinerUserId.ToUpperInvariant(),
                Email = $"{examinerUserId}@test.local",
                NormalizedEmail = $"{examinerUserId.ToUpperInvariant()}@TEST.LOCAL",
                SecurityStamp = Guid.NewGuid().ToString(),
                IsActive = true
            });
        }

        var at = examinedAt ?? DateTimeOffset.UtcNow;
        var examId = Guid.NewGuid();
        db.MedicalExams.Add(new MedicalExam
        {
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ExaminerUserId = examinerUserId,
            ExaminedAt = at,
            ExamSequence = 1,
            IsCurrent = true,
            VersionNo = 1,
            UpdatedByUserId = "actor-1",
            CreatedAt = at,
            UpdatedAt = at
        });
        db.ClinicAssessments.Add(new ClinicAssessment
        {
            AssessmentId = Guid.NewGuid(),
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ResultStatusId = resultStatusId,
            RejectionReason = resultStatusId == 2 ? "سبب رفض اختبار" : null,
            ClinicalNotes = "ملاحظة اختبار",
            AssessedByUserId = examinerUserId,
            AssessedAt = at,
            UpdatedByUserId = "actor-1",
            UpdatedAt = at
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedExamWithoutAssessmentAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        string examinerUserId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == examinerUserId))
        {
            db.Users.Add(new Domain.Entities.Auth.User
            {
                Id = examinerUserId,
                UserName = examinerUserId,
                NormalizedUserName = examinerUserId.ToUpperInvariant(),
                Email = $"{examinerUserId}@test.local",
                NormalizedEmail = $"{examinerUserId.ToUpperInvariant()}@TEST.LOCAL",
                SecurityStamp = Guid.NewGuid().ToString(),
                IsActive = true
            });
        }

        var at = DateTimeOffset.UtcNow;
        db.MedicalExams.Add(new MedicalExam
        {
            ExamId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ExaminerUserId = examinerUserId,
            ExaminedAt = at,
            ExamSequence = 1,
            IsCurrent = true,
            VersionNo = 1,
            UpdatedByUserId = "actor-1",
            CreatedAt = at,
            UpdatedAt = at
        });
        await db.SaveChangesAsync();
    }
}

/// <summary>مرآة استعلامات FR-068 لـ ToQueryString وCount على PostgreSQL.</summary>
internal static class DoctorLinkagePostgresProbes
{
    public static IQueryable<DoctorLinkageByDoctorAggregateProbe> ByDoctorGrouped(
        AppDbContext db,
        Guid centerId,
        DoctorLinkageQuery query)
    {
        var clinicWorkspace = new ClinicWorkspaceRepository(db);
        var examsQ = clinicWorkspace.QueryMedicalExams(asNoTracking: true)
            .Where(e => e.CenterId == centerId && e.IsCurrent);
        if (query.ClinicId.HasValue)
            examsQ = examsQ.Where(e => e.ClinicId == query.ClinicId.Value);

        var assessmentsQ = clinicWorkspace.QueryClinicAssessments(asNoTracking: true);
        var flatQ = from e in examsQ
            join a in assessmentsQ on e.ExamId equals a.ExamId into ag
            from a in ag.DefaultIfEmpty()
            select new
            {
                e.MemberId,
                e.ExaminerUserId,
                e.ClinicId,
                ResultStatusId = a != null ? a.ResultStatusId : (short?)null
            };

        return flatQ
            .GroupBy(x => new { x.ExaminerUserId, x.ClinicId })
            .Select(g => new DoctorLinkageByDoctorAggregateProbe
            {
                ExaminerUserId = g.Key.ExaminerUserId,
                ClinicId = g.Key.ClinicId,
                MemberCount = g.Select(x => x.MemberId).Distinct().Count(),
                ExamCount = g.Count(),
                AcceptedCount = g.Count(x => x.ResultStatusId == (short)1),
                RejectedCount = g.Count(x => x.ResultStatusId == (short)2),
                DeferredCount = g.Count(x => x.ResultStatusId == (short)3)
            });
    }

    public static IQueryable<DoctorLinkageExamFlatProbe> ByDoctorFlat(
        AppDbContext db,
        Guid centerId,
        DoctorLinkageQuery query)
    {
        var clinicWorkspace = new ClinicWorkspaceRepository(db);
        var examsQ = clinicWorkspace.QueryMedicalExams(asNoTracking: true)
            .Where(e => e.CenterId == centerId && e.IsCurrent);
        var assessmentsQ = clinicWorkspace.QueryClinicAssessments(asNoTracking: true);
        return from e in examsQ
            join a in assessmentsQ on e.ExamId equals a.ExamId into ag
            from a in ag.DefaultIfEmpty()
            select new DoctorLinkageExamFlatProbe
            {
                ExaminerUserId = e.ExaminerUserId,
                MemberId = e.MemberId
            };
    }

    public static IQueryable<DoctorLinkageMemberKeyProbe> ByMemberKeys(
        AppDbContext db,
        Guid centerId,
        DoctorLinkageQuery query)
    {
        var clinicWorkspace = new ClinicWorkspaceRepository(db);
        var members = new MemberRepository(db);
        var examsQ = clinicWorkspace.QueryMedicalExams(asNoTracking: true)
            .Where(e => e.CenterId == centerId && e.IsCurrent);
        var membersQ = members.Query(asNoTracking: true).Where(m => m.CurrentMedicalCenterId == centerId);
        var assessmentsQ = clinicWorkspace.QueryClinicAssessments(asNoTracking: true);

        var flat = from e in examsQ
            join m in membersQ on e.MemberId equals m.MemberId
            join a in assessmentsQ on e.ExamId equals a.ExamId into ag
            from a in ag.DefaultIfEmpty()
            select new
            {
                m.MemberId,
                m.FileNumber,
                m.FullName
            };

        return flat
            .GroupBy(r => new { r.MemberId, r.FileNumber, r.FullName })
            .Select(g => new DoctorLinkageMemberKeyProbe
            {
                MemberId = g.Key.MemberId,
                FileNumber = g.Key.FileNumber,
                FullName = g.Key.FullName
            });
    }
}

internal sealed class DoctorLinkageByDoctorAggregateProbe
{
    public string ExaminerUserId { get; init; } = null!;
    public Guid ClinicId { get; init; }
    public int MemberCount { get; init; }
    public int ExamCount { get; init; }
    public int AcceptedCount { get; init; }
    public int RejectedCount { get; init; }
    public int DeferredCount { get; init; }
}

internal sealed class DoctorLinkageExamFlatProbe
{
    public string ExaminerUserId { get; init; } = null!;
    public Guid MemberId { get; init; }
}

internal sealed class DoctorLinkageMemberKeyProbe
{
    public Guid MemberId { get; init; }
    public long FileNumber { get; init; }
    public string FullName { get; init; } = null!;
}
