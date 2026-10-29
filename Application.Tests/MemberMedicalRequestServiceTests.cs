using Application.Common.Permissions;
using Application.Dtos.ClinicWorkspace;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.Services.ClinicWorkspace;
using Domain.Entities.Core;
using Domain.Entities.Medical;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Application.Tests;

public class MemberMedicalRequestServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Get_empty_member_returns_explicit_empty_state()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "AGG-EMPTY");
        var service = CreateService(db, CenterA, allowAttachments: true);

        var result = await service.GetForMemberAsync(memberId);

        Assert.True(result.IsEmpty);
        Assert.Equal(MemberMedicalRequestService.EmptyMessageAr, result.EmptyMessageAr);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.PendingCount);
        Assert.Empty(result.Items);
        Assert.True(result.AttachmentsIncluded);
        Assert.Equal(CenterA, result.CenterId);
        Assert.Equal(memberId, result.MemberId);
    }

    [Fact]
    public async Task Get_returns_consultations_and_investigations_with_classification_fields()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "AGG-MULTI");
        await SeedConsultationAsync(db, memberId, CenterA, ClinicEyes, "PENDING", "سبب إحالة");
        await SeedConsultationAsync(db, memberId, CenterA, ClinicEnt, "COMPLETED", "سبب ثانٍ", "نتيجة");
        await SeedInvestigationAsync(db, memberId, CenterA, ClinicEyes, "PENDING", "CBC", "سبب مخبري");
        await SeedInvestigationAsync(db, memberId, CenterA, ClinicEnt, "COMPLETED", "XRay", "سبب شعاعي", "طبيعي");

        var service = CreateService(db, CenterA, allowAttachments: true);
        var result = await service.GetForMemberAsync(memberId);

        Assert.False(result.IsEmpty);
        Assert.Null(result.EmptyMessageAr);
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(2, result.PendingCount);
        Assert.Equal(2, result.CompletedCount);

        Assert.Equal(2, result.Items.Count(i =>
            i.RequestKind == MemberMedicalRequestService.RequestKindConsultation));
        Assert.Equal(2, result.Items.Count(i =>
            i.RequestKind == MemberMedicalRequestService.RequestKindInvestigation));
        Assert.Equal(2, result.Items.Count(i => i.IsPending));

        var eyesConsultation = Assert.Single(result.Items, i =>
            i.RequestKind == MemberMedicalRequestService.RequestKindConsultation
            && i.ClinicId == ClinicEyes);
        Assert.Equal("العيون", eyesConsultation.ClinicNameAr);
        Assert.Equal("العيون", eyesConsultation.SpecialtyNameAr);
        Assert.Equal("EYE", eyesConsultation.SpecialtyCode);
        Assert.Equal("قلبية", eyesConsultation.TypeNameAr);
        Assert.Equal("سبب إحالة", eyesConsultation.RequestReason);
        Assert.True(eyesConsultation.IsPending);
        Assert.Null(eyesConsultation.ResultText);

        var completedInv = Assert.Single(result.Items, i =>
            i.RequestKind == MemberMedicalRequestService.RequestKindInvestigation
            && i.Status == "COMPLETED");
        Assert.Equal("طبيعي", completedInv.ResultText);
        Assert.Equal("XRay", completedInv.TestName);
        Assert.False(completedInv.IsPending);
    }

    [Fact]
    public async Task Get_includes_attachment_metadata_without_storage_key()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "AGG-ATT");
        var consultationId = await SeedConsultationAsync(
            db, memberId, CenterA, ClinicEyes, "PENDING", "سبب");
        var fileId = await SeedAttachmentAsync(
            db, memberId, CenterA, consultationId: consultationId, storageKey: "private/secret/path.pdf");

        var service = CreateService(db, CenterA, allowAttachments: true);
        var result = await service.GetForMemberAsync(memberId);

        Assert.True(result.AttachmentsIncluded);
        var item = Assert.Single(result.Items);
        var attachment = Assert.Single(item.Attachments);
        Assert.Equal(fileId, attachment.FileAssetId);
        Assert.Equal("report.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal($"/api/Attachments/download/{fileId}", attachment.DownloadPath);
        Assert.DoesNotContain("private/secret", attachment.DownloadPath);
        Assert.DoesNotContain("storage", attachment.FileName, StringComparison.OrdinalIgnoreCase);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain("private/secret/path.pdf", json);
        Assert.DoesNotContain("StorageKey", json);
    }

    [Fact]
    public async Task Get_omits_attachments_when_AttachmentsRead_missing()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "AGG-NOATT");
        var consultationId = await SeedConsultationAsync(
            db, memberId, CenterA, ClinicEyes, "COMPLETED", "سبب", "نتيجة");
        await SeedAttachmentAsync(db, memberId, CenterA, consultationId: consultationId);

        var service = CreateService(db, CenterA, allowAttachments: false);
        var result = await service.GetForMemberAsync(memberId);

        Assert.False(result.AttachmentsIncluded);
        var item = Assert.Single(result.Items);
        Assert.Empty(item.Attachments);
        Assert.Equal("نتيجة", item.ResultText);
    }

    [Fact]
    public async Task Get_rejects_member_from_other_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "AGG-XCTR");
        await SeedConsultationAsync(db, memberId, CenterA, ClinicEyes, "PENDING", "سبب");

        var serviceB = CreateService(db, CenterB, allowAttachments: true);

        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            serviceB.GetForMemberAsync(memberId));
    }

    [Fact]
    public async Task Get_does_not_leak_other_center_records_for_same_user_context()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberA = await CreateMemberAsync(db, CenterA, "AGG-ISO-A");
        var memberB = await CreateMemberAsync(db, CenterB, "AGG-ISO-B");
        await SeedConsultationAsync(db, memberA, CenterA, ClinicEyes, "PENDING", "مركز أ");
        await SeedConsultationAsync(db, memberB, CenterB, ClinicEyes, "PENDING", "مركز ب");
        await SeedInvestigationAsync(db, memberB, CenterB, ClinicEyes, "PENDING", "CBC", "مخفي");

        var service = CreateService(db, CenterA, allowAttachments: true);
        var result = await service.GetForMemberAsync(memberA);

        Assert.Equal(1, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal(CenterA, result.CenterId));
        Assert.DoesNotContain(result.Items, i => i.RequestReason == "مركز ب");
        Assert.DoesNotContain(result.Items, i => i.TestName == "CBC");
    }

    [Fact]
    public async Task Get_rejects_missing_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "AGG-PERM");
        var service = CreateService(db, CenterA, allowConsultations: false, allowAttachments: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.GetForMemberAsync(memberId));
    }

    [Fact]
    public async Task Get_rejects_empty_member_id()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var service = CreateService(db, CenterA, allowAttachments: true);

        await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.GetForMemberAsync(Guid.Empty));
    }

    private static MemberMedicalRequestService CreateService(
        AppDbContext db,
        Guid centerId,
        bool allowConsultations = true,
        bool allowAttachments = true)
    {
        var gate = new Mock<IPermissionGate>();
        if (allowConsultations)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(centerId);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        gate.Setup(g => g.Has(MilitaryHealthPermissions.Medical.AttachmentsRead))
            .Returns(allowAttachments);

        return new MemberMedicalRequestService(
            new ConsultationRepository(db),
            new InvestigationRepository(db),
            new AttachmentRepository(db),
            new MemberRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            gate.Object);
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, Guid centerId, string recruitment)
    {
        var members = MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = "منتسب عرض مجمّع",
            MotherName = "الأم",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-25),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentNumber = recruitment,
            RecruitmentCenterId = RecruitmentCenterId,
            BloodTypeId = 1,
            HeightCm = 170,
            WeightKg = 66,
            BloodPressure = "120/80",
            PulseBpm = 72,
            HasTattoo = false
        });
        return created.MemberId;
    }

    private static async Task<Guid> SeedConsultationAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        string status,
        string reason,
        string? resultText = null)
    {
        var id = Guid.NewGuid();
        db.Consultations.Add(new Consultation
        {
            ConsultationId = id,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            RequestedByUserId = "actor-1",
            ConsultationTypeId = 1,
            ReferralReason = reason,
            Status = status,
            ResultText = resultText,
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-status.Length),
            ResultedAt = resultText is null ? null : DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedInvestigationAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        string status,
        string testName,
        string reason,
        string? resultText = null)
    {
        var id = Guid.NewGuid();
        db.Investigations.Add(new Investigation
        {
            InvestigationId = id,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            RequestedByUserId = "actor-1",
            InvestigationTypeId = testName.Contains("X", StringComparison.OrdinalIgnoreCase) ? (short)2 : (short)1,
            TestName = testName,
            RequestReason = reason,
            Status = status,
            ResultText = resultText,
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-testName.Length),
            ResultedAt = resultText is null ? null : DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedAttachmentAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid? consultationId = null,
        Guid? investigationId = null,
        string storageKey = "storage/hidden/key.bin")
    {
        var fileId = Guid.NewGuid();
        db.FileAssets.Add(new FileAsset
        {
            FileAssetId = fileId,
            CenterId = centerId,
            StorageKey = storageKey,
            OriginalName = "report.pdf",
            MimeType = "application/pdf",
            Extension = ".pdf",
            SizeBytes = 2048,
            UploadedByUserId = "actor-1",
            CreatedAt = DateTimeOffset.UtcNow,
            IsDeleted = false
        });
        db.MedicalAttachments.Add(new MedicalAttachment
        {
            AttachmentId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = centerId,
            FileAssetId = fileId,
            ConsultationId = consultationId,
            InvestigationId = investigationId,
            UploadedByUserId = "actor-1",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return fileId;
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedClinicAsync(db, ClinicEnt, "ENT", "الأذنية", 2);
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedCenterClinicAsync(db, CenterA, ClinicEnt);
        await SeedCenterClinicAsync(db, CenterB, ClinicEyes);

        if (!await db.ConsultationTypes.AnyAsync(t => t.ConsultationTypeId == 1))
        {
            db.ConsultationTypes.Add(new ConsultationType
            {
                ConsultationTypeId = 1,
                Code = "CARDIO",
                NameAr = "قلبية",
                SortOrder = 1,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        if (!await db.InvestigationTypes.AnyAsync(t => t.InvestigationTypeId == 1))
        {
            db.InvestigationTypes.Add(new InvestigationType
            {
                InvestigationTypeId = 1,
                Code = "LAB",
                NameAr = "مخبري",
                SortOrder = 1,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        if (!await db.InvestigationTypes.AnyAsync(t => t.InvestigationTypeId == 2))
        {
            db.InvestigationTypes.Add(new InvestigationType
            {
                InvestigationTypeId = 2,
                Code = "RAD",
                NameAr = "شعاعي",
                SortOrder = 2,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
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
}
