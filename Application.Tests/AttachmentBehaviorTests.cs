using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Application.Common.Permissions;
using Application.Dtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AttachmentService;
using Application.Services.FileStorageService;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Medical;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Application.Tests;

public class AttachmentBehaviorTests
{
    private static readonly Guid CenterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCenterId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Upload_with_active_center_creates_file_and_consultation_link()
    {
        var consultationId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var files = new Mock<IAttachmentRepository>();
        MedicalAttachment? linked = null;
        files.Setup(f => f.FindConsultationAsync(consultationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileLinkTarget(consultationId, memberId, CenterId));
        files.Setup(f => f.AddMedicalAttachment(It.IsAny<MedicalAttachment>()))
            .Callback<MedicalAttachment>(a => linked = a);

        var service = CreateService(files, centerId: CenterId, assigned: true);
        var result = await service.UploadAsync(consultationId.ToString(), "Consultation", PdfFile(), null);

        Assert.NotNull(linked);
        Assert.Equal(consultationId, linked!.ConsultationId);
        Assert.Null(linked.InvestigationId);
        Assert.Equal(CenterId, linked.CenterId);
        Assert.Equal(memberId, linked.MemberId);
        Assert.Equal(linked.FileAssetId, result.FileAssetId);
    }

    [Fact]
    public async Task Upload_without_active_center_is_rejected_for_a_local_operation()
    {
        var files = new Mock<IAttachmentRepository>();
        var service = CreateService(files, centerId: null, assigned: true, central: true);

        await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.UploadAsync(Guid.NewGuid().ToString(), "Consultation", PdfFile(), null));

        files.Verify(f => f.Add(It.IsAny<FileAsset>()), Times.Never);
    }

    [Fact]
    public async Task Link_across_centers_is_rejected()
    {
        var fileId = Guid.NewGuid();
        var files = new Mock<IAttachmentRepository>();
        files.Setup(f => f.GetTrackedByIdAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileAsset { FileAssetId = fileId, CenterId = OtherCenterId, StorageKey = "a", OriginalName = "a.pdf", MimeType = "application/pdf", Extension = ".pdf", UploadedByUserId = "user-1" });

        var service = CreateService(files, CenterId, assigned: true);

        await Assert.ThrowsAsync<RoleNotFoundException>(() =>
            service.LinkToEntityAsync(fileId, Guid.NewGuid().ToString(), "Consultation"));

        files.Verify(f => f.AddMedicalAttachment(It.IsAny<MedicalAttachment>()), Times.Never);
    }

    [Fact]
    public async Task Member_attachments_are_loaded_from_the_repository()
    {
        var memberId = Guid.NewGuid();
        var file = SampleFile(CenterId);
        var files = new Mock<IAttachmentRepository>();
        files.Setup(f => f.FindMemberAsync(memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileLinkTarget(memberId, memberId, CenterId));
        files.Setup(f => f.ListMemberFilesAsync(memberId, CenterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileAsset> { file });

        var service = CreateService(files, CenterId, assigned: true);
        var list = await service.GetAttachmentsForEntityAsync(memberId.ToString(), "Member");

        Assert.Single(list);
        Assert.Equal(file.FileAssetId, list[0].FileAssetId);
    }

    [Fact]
    public async Task Link_creates_a_consultation_attachment()
    {
        var fileId = Guid.NewGuid();
        var consultationId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var files = new Mock<IAttachmentRepository>();
        MedicalAttachment? linked = null;
        files.Setup(f => f.GetTrackedByIdAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleFile(CenterId, fileId));
        files.Setup(f => f.FindConsultationAsync(consultationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileLinkTarget(consultationId, memberId, CenterId));
        files.Setup(f => f.AddMedicalAttachment(It.IsAny<MedicalAttachment>()))
            .Callback<MedicalAttachment>(a => linked = a);

        var service = CreateService(files, CenterId, assigned: true);
        await service.LinkToEntityAsync(fileId, consultationId.ToString(), "Consultation");

        Assert.NotNull(linked);
        Assert.Equal(consultationId, linked!.ConsultationId);
        Assert.Null(linked.InvestigationId);
    }

    [Fact]
    public async Task Link_creates_an_investigation_attachment()
    {
        var fileId = Guid.NewGuid();
        var investigationId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var files = new Mock<IAttachmentRepository>();
        MedicalAttachment? linked = null;
        files.Setup(f => f.GetTrackedByIdAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleFile(CenterId, fileId));
        files.Setup(f => f.FindInvestigationAsync(investigationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileLinkTarget(investigationId, memberId, CenterId));
        files.Setup(f => f.AddMedicalAttachment(It.IsAny<MedicalAttachment>()))
            .Callback<MedicalAttachment>(a => linked = a);

        var service = CreateService(files, CenterId, assigned: true);
        await service.LinkToEntityAsync(fileId, investigationId.ToString(), "Investigation");

        Assert.NotNull(linked);
        Assert.Equal(investigationId, linked!.InvestigationId);
        Assert.Null(linked.ConsultationId);
    }

    [Fact]
    public async Task Zip_download_contains_the_linked_file_bytes()
    {
        var consultationId = Guid.NewGuid();
        var file = SampleFile(CenterId);
        file.OriginalName = "report.pdf";
        var payload = Encoding.UTF8.GetBytes("medical-file");
        var files = new Mock<IAttachmentRepository>();
        files.Setup(f => f.ListConsultationFilesAsync(consultationId, CenterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileAsset> { file });

        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.ReadFileBytes(file.StorageKey)).Returns(payload);
        var service = CreateService(files, CenterId, assigned: true, storage: storage.Object);

        var (bytes, name) = await service.DownloadZipForEntityAsync(consultationId.ToString(), "Consultation");

        Assert.EndsWith(".zip", name);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("report.pdf", entry.Name);
        using var reader = new StreamReader(entry.Open());
        Assert.Equal("medical-file", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Medical_file_read_requires_attachments_permission()
    {
        var file = SampleFile(CenterId);
        var files = new Mock<IAttachmentRepository>();
        files.Setup(f => f.FindByIdAsync(file.FileAssetId, It.IsAny<CancellationToken>())).ReturnsAsync(file);
        files.Setup(f => f.IsProfilePhotoAsync("user-1", file.FileAssetId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var service = CreateService(files, CenterId, assigned: true, allowMedicalRead: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() => service.GetByIdAsync(file.FileAssetId));
    }

    [Fact]
    public async Task Deleted_file_cannot_be_downloaded()
    {
        var files = new Mock<IAttachmentRepository>();
        files.Setup(f => f.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileAsset?)null);
        var service = CreateService(files, CenterId, assigned: true);

        await Assert.ThrowsAsync<RoleNotFoundException>(() => service.DownloadAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task User_profile_photo_is_linked_through_file_asset()
    {
        var profile = new UserProfile { UserId = "user-1", EmployeeNumber = "E-1", RankOrTitle = "نقيب", UpdatedByUserId = "user-1" };
        var files = new Mock<IAttachmentRepository>();
        files.Setup(f => f.FindTrackedProfileAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(profile);

        var service = CreateService(files, CenterId, assigned: true);
        var result = await service.UploadAsync("user-1", "UserProfile", PdfFile(), null);

        Assert.Equal(result.FileAssetId, profile.ProfileFileAssetId);
        files.Verify(f => f.AddMedicalAttachment(It.IsAny<MedicalAttachment>()), Times.Never);
    }

    [Fact]
    public async Task Central_user_with_profile_can_upload_photo_without_active_center()
    {
        var profile = new UserProfile
        {
            UserId = "user-1",
            EmployeeNumber = "E-100",
            RankOrTitle = "نقيب",
            UpdatedByUserId = "user-1"
        };
        var files = new Mock<IAttachmentRepository>();
        files.Setup(f => f.FindTrackedProfileAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        files.Setup(f => f.FindActiveAssignmentCenterIdAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        files.Setup(f => f.FindFirstActiveCenterIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        var service = CreateService(files, null, assigned: false, central: true);
        var result = await service.UploadAsync("user-1", "UserProfile", PdfFile(), null);

        Assert.Equal(result.FileAssetId, profile.ProfileFileAssetId);
        Assert.Equal("E-100", profile.EmployeeNumber);
        Assert.Equal("نقيب", profile.RankOrTitle);
        files.Verify(f => f.AddProfile(It.IsAny<UserProfile>()), Times.Never);
        files.Verify(f => f.AddMedicalAttachment(It.IsAny<MedicalAttachment>()), Times.Never);
    }

    [Fact]
    public async Task Profile_photo_upload_rejects_when_job_profile_is_missing()
    {
        var files = new Mock<IAttachmentRepository>();
        files.Setup(f => f.FindTrackedProfileAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync((UserProfile?)null);
        files.Setup(f => f.FindActiveAssignmentCenterIdAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        files.Setup(f => f.FindFirstActiveCenterIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        var service = CreateService(files, null, assigned: false, central: true);
        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.UploadAsync("user-1", "UserProfile", PdfFile(), null));

        Assert.Contains("البيانات الوظيفية", error.Message);
        files.Verify(f => f.AddProfile(It.IsAny<UserProfile>()), Times.Never);
    }

    [Fact]
    public void ReadFileBytes_rejects_path_traversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "mhs-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var env = new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
            env.SetupGet(e => e.ContentRootPath).Returns(root);
            var config = new ConfigurationBuilder().Build();
            var storage = new FileStorageService(env.Object, config);

            Assert.Throws<InvalidFileException>(() => storage.ReadFileBytes("../outside.txt"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Upload_medical_requires_consultations_investigations_permission()
    {
        var files = new Mock<IAttachmentRepository>();
        var service = CreateService(files, CenterId, assigned: true, allowMedicalWrite: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.UploadAsync(Guid.NewGuid().ToString(), "Consultation", PdfFile(), null));

        files.Verify(f => f.Add(It.IsAny<FileAsset>()), Times.Never);
    }

    [Fact]
    public async Task Download_other_center_is_not_found()
    {
        var file = SampleFile(OtherCenterId);
        var files = new Mock<IAttachmentRepository>();
        files.Setup(f => f.FindByIdAsync(file.FileAssetId, It.IsAny<CancellationToken>())).ReturnsAsync(file);
        files.Setup(f => f.IsProfilePhotoAsync("user-1", file.FileAssetId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var service = CreateService(files, CenterId, assigned: true);
        var error = await Assert.ThrowsAsync<RoleNotFoundException>(() => service.DownloadAsync(file.FileAssetId));
        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task Upload_medical_writes_audit_without_storage_path_or_bytes()
    {
        var consultationId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var files = new Mock<IAttachmentRepository>();
        files.Setup(f => f.FindConsultationAsync(consultationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileLinkTarget(consultationId, memberId, CenterId));

        AuditEvent? audit = null;
        var auditRepo = new Mock<IAuditLogRepository>();
        auditRepo.Setup(a => a.Add(It.IsAny<AuditEvent>())).Callback<AuditEvent>(e => audit = e);

        var service = CreateService(files, CenterId, assigned: true, audit: auditRepo.Object);
        await service.UploadAsync(consultationId.ToString(), "Consultation", PdfFile(), null);

        Assert.NotNull(audit);
        Assert.Equal(AttachmentService.MedicalAttachmentUploadedEventType, audit!.EventType);
        Assert.DoesNotContain("storage/", audit.NewValues ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%PDF", audit.NewValues ?? "", StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(audit.NewValues!);
        Assert.True(doc.RootElement.TryGetProperty("Sha256", out _));
        Assert.True(doc.RootElement.TryGetProperty("SizeBytes", out _));
        Assert.False(doc.RootElement.TryGetProperty("StorageKey", out _));
    }

    [Fact]
    public async Task Download_missing_physical_file_is_not_found()
    {
        var file = SampleFile(CenterId);
        var files = new Mock<IAttachmentRepository>();
        files.Setup(f => f.FindByIdAsync(file.FileAssetId, It.IsAny<CancellationToken>())).ReturnsAsync(file);
        files.Setup(f => f.IsProfilePhotoAsync("user-1", file.FileAssetId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        files.Setup(f => f.HasMedicalLinkAsync(file.FileAssetId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.ReadFileBytes(file.StorageKey)).Throws(new InvalidFileException("missing"));

        var service = CreateService(files, CenterId, assigned: true, storage: storage.Object);
        var error = await Assert.ThrowsAsync<RoleNotFoundException>(() => service.DownloadAsync(file.FileAssetId));
        Assert.Contains("تالف", error.Message);
    }

    [Fact]
    public async Task Download_medical_writes_audit_without_file_bytes()
    {
        var file = SampleFile(CenterId);
        var files = new Mock<IAttachmentRepository>();
        files.Setup(f => f.FindByIdAsync(file.FileAssetId, It.IsAny<CancellationToken>())).ReturnsAsync(file);
        files.Setup(f => f.IsProfilePhotoAsync("user-1", file.FileAssetId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        files.Setup(f => f.HasMedicalLinkAsync(file.FileAssetId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        AuditEvent? audit = null;
        var auditRepo = new Mock<IAuditLogRepository>();
        auditRepo.Setup(a => a.Add(It.IsAny<AuditEvent>())).Callback<AuditEvent>(e => audit = e);

        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.ReadFileBytes(file.StorageKey)).Returns(Encoding.UTF8.GetBytes("SECRET-BYTES"));

        var service = CreateService(files, CenterId, assigned: true, storage: storage.Object, audit: auditRepo.Object);
        await service.DownloadAsync(file.FileAssetId);

        Assert.NotNull(audit);
        Assert.Equal(AttachmentService.MedicalAttachmentDownloadedEventType, audit!.EventType);
        Assert.DoesNotContain("SECRET-BYTES", audit.NewValues ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("storage/", audit.NewValues ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateFile_rejects_disallowed_extension_and_oversized_and_spoofed_signature()
    {
        var root = Path.Combine(Path.GetTempPath(), "mhs-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var env = new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
            env.SetupGet(e => e.ContentRootPath).Returns(root);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FileStorage:MaxFileSizeInMB"] = "1",
                    ["FileStorage:AllowedExtensions:0"] = ".pdf",
                    ["FileStorage:AllowedExtensions:1"] = ".png"
                })
                .Build();
            var storage = new FileStorageService(env.Object, config);

            Assert.False(storage.ValidateFile(NamedFile("bad.exe", new byte[] { 0x4D, 0x5A }), out var extErr));
            Assert.Contains("غير مسموح", extErr);

            var oversized = new byte[2 * 1024 * 1024];
            oversized[0] = 0x25; oversized[1] = 0x50; oversized[2] = 0x44; oversized[3] = 0x46;
            Assert.False(storage.ValidateFile(NamedFile("big.pdf", oversized), out var sizeErr));
            Assert.Contains("الحد المسموح", sizeErr);

            Assert.False(storage.ValidateFile(NamedFile("fake.pdf", Encoding.UTF8.GetBytes("not-a-pdf")), out var sigErr));
            Assert.Contains("الحقيقي", sigErr);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Dto_mapping_nulls_storage_path()
    {
        var files = new Mock<IAttachmentRepository>();
        var service = CreateService(files, CenterId, assigned: true);
        var file = SampleFile(CenterId);
        files.Setup(f => f.FindByIdAsync(file.FileAssetId, It.IsAny<CancellationToken>())).ReturnsAsync(file);
        files.Setup(f => f.IsProfilePhotoAsync("user-1", file.FileAssetId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var dto = await service.GetByIdAsync(file.FileAssetId);
        Assert.Null(dto.FilePath);
        Assert.Contains("/api/attachments/download/", dto.FileUrl);
    }

    private static AttachmentService CreateService(
        Mock<IAttachmentRepository> files,
        Guid? centerId,
        bool assigned,
        bool central = false,
        IFileStorageService? storage = null,
        bool allowMedicalRead = true,
        bool allowMedicalWrite = true,
        IAuditLogRepository? audit = null)
    {
        var unit = new Mock<IUnitOfWork>();
        unit.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<AttachmentReadDto>(It.IsAny<object>()))
            .Returns((object source) =>
            {
                var file = (FileAsset)source;
                return new AttachmentReadDto { AttachmentId = file.FileAssetId, FileName = file.OriginalName };
            });

        storage ??= new Mock<IFileStorageService>().Also(s =>
        {
            s.Setup(x => x.ValidateFile(It.IsAny<IFormFile>(), out It.Ref<string>.IsAny)).Returns(true);
            s.Setup(x => x.SaveFileAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(("storage/uploads/test.pdf", 4L));
            s.Setup(x => x.DeleteFileAsync(It.IsAny<string>())).ReturnsAsync(true);
        }).Object;

        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns((HttpContext?)null);

        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.UserId).Returns("user-1");

        var center = new Mock<ICurrentCenterService>();
        center.SetupGet(c => c.CenterId).Returns(centerId);
        center.SetupGet(c => c.IsCentralContext).Returns(central || centerId is null);
        center.SetupGet(c => c.UserId).Returns("user-1");

        var access = new Mock<IAccessContextRepository>();
        access.Setup(a => a.IsActivelyAssignedAsync("user-1", It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(assigned);

        var activeCenters = new Mock<IActiveCenterService>();
        activeCenters
            .Setup(s => s.RequireOperationalCenterAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (central || centerId is null)
                    throw new InvalidUserDataException("العملية المحلية تتطلب مركزاً نشطاً.");
                if (!assigned)
                    throw new ForbiddenPermissionException("المستخدم غير مسند إلى المركز النشط أو أن الإسناد لم يعد فعالاً.");
                return Task.FromResult(centerId.Value);
            });

        var permissions = new Mock<IPermissionGate>();
        permissions
            .Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Medical.AttachmentsRead,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (!allowMedicalRead)
                    return Task.FromException<Guid>(new ForbiddenPermissionException());
                if (central || centerId is null)
                    return Task.FromException<Guid>(new InvalidUserDataException("العملية المحلية تتطلب مركزاً نشطاً."));
                return Task.FromResult(centerId.Value);
            });

        permissions
            .Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (!allowMedicalWrite)
                    return Task.FromException<Guid>(new ForbiddenPermissionException());
                if (central || centerId is null)
                    return Task.FromException<Guid>(new InvalidUserDataException("العملية المحلية تتطلب مركزاً نشطاً."));
                return Task.FromResult(centerId.Value);
            });

        audit ??= Mock.Of<IAuditLogRepository>();

        return new AttachmentService(
            files.Object,
            unit.Object,
            mapper.Object,
            storage,
            http.Object,
            user.Object,
            center.Object,
            access.Object,
            activeCenters.Object,
            permissions.Object,
            audit);
    }

    private static FileAsset SampleFile(Guid centerId, Guid? id = null) => new()
    {
        FileAssetId = id ?? Guid.NewGuid(),
        CenterId = centerId,
        StorageKey = "storage/uploads/report.pdf",
        OriginalName = "report.pdf",
        MimeType = "application/pdf",
        Extension = ".pdf",
        SizeBytes = 4,
        UploadedByUserId = "user-1"
    };

    private static FormFile PdfFile()
        => NamedFile("note.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf");

    private static FormFile NamedFile(string name, byte[] bytes, string contentType = "application/octet-stream")
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}

static class MockExtensions
{
    public static Mock<T> Also<T>(this Mock<T> mock, Action<Mock<T>> configure) where T : class
    {
        configure(mock);
        return mock;
    }
}
