using System.Net;
using System.Text.Json;
using Application.Common.Permissions;
using Application.Common.Results;
using Application.Common.Security;
using Application.Dtos.ExportDtos;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Members;

/// <summary>
/// خدمات ملف المنتسب: محلي (تسجيل/تعديل/إيصال/Excel) ومركزي (بحث/عرض مع تدقيق اطلاع عابر).
/// </summary>
public class MemberService : IMemberService
{
    public const int MaxExportRows = 10000;

    private static readonly JsonSerializerOptions ImportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IMemberRepository _members;
    private readonly IMemberImportBatchRepository _importBatches;
    private readonly IMedicalCenterRepository _centers;
    private readonly IReferenceDataRepository _references;
    private readonly IAuditLogRepository _audit;
    private readonly ICrossCenterAccessService _crossCenterAccess;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;
    private readonly IExportService _export;

    public MemberService(
        IMemberRepository members,
        IMemberImportBatchRepository importBatches,
        IMedicalCenterRepository centers,
        IReferenceDataRepository references,
        IAuditLogRepository audit,
        ICrossCenterAccessService crossCenterAccess,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        IPermissionGate permissionGate,
        ICurrentUserService currentUser,
        IExportService export)
    {
        _members = members;
        _importBatches = importBatches;
        _centers = centers;
        _references = references;
        _audit = audit;
        _crossCenterAccess = crossCenterAccess;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _permissionGate = permissionGate;
        _currentUser = currentUser;
        _export = export;
    }

    public async Task<PaginatedList<MemberListItemDto>> ListAsync(
        MemberListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.View,
            clientCenterId,
            cancellationToken);

        return await ExecuteListAsync(query, centerId, cancellationToken);
    }

    public async Task<PaginatedList<MemberListItemDto>> ListCentralAsync(
        MemberListQuery query,
        string accessReason,
        Guid? targetCenterId = null,
        CancellationToken cancellationToken = default)
    {
        await _permissionGate.EnsureCentralAsync(
            MilitaryHealthPermissions.Members.ViewCentral,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(accessReason))
            throw new InvalidMemberDataException("سبب الاطلاع العابر مطلوب.");

        var page = await ExecuteListAsync(query, targetCenterId, cancellationToken);

        var criteria = BuildListCriteria(query, targetCenterId);
        var centersToAudit = page.Items
            .Select(i => i.CurrentMedicalCenterId)
            .Distinct()
            .ToList();

        if (centersToAudit.Count == 0 && targetCenterId.HasValue)
            centersToAudit.Add(targetCenterId.Value);

        foreach (var centerId in centersToAudit)
        {
            await _crossCenterAccess.RecordAsync(
                centerId,
                accessReason.Trim(),
                dataType: "Member",
                queryCriteria: criteria,
                entityType: nameof(Member),
                entityId: null,
                cancellationToken);
        }

        return page;
    }

    public async Task<MemberDetailsDto> GetByIdAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.View,
            clientCenterId,
            cancellationToken);

        var member = await _members.GetByIdInCenterAsync(memberId, centerId, asNoTracking: true, cancellationToken)
            ?? throw new MemberNotFoundException();

        return await ToDetailsAsync(member, cancellationToken);
    }

    public async Task<MemberDetailsDto> GetByIdCentralAsync(
        Guid memberId,
        string accessReason,
        CancellationToken cancellationToken = default)
    {
        await _permissionGate.EnsureCentralAsync(
            MilitaryHealthPermissions.Members.ViewCentral,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(accessReason))
            throw new InvalidMemberDataException("سبب الاطلاع العابر مطلوب.");

        var member = await _members.GetByIdAsync(memberId, asNoTracking: true, cancellationToken)
            ?? throw new MemberNotFoundException();

        var details = await ToDetailsAsync(member, cancellationToken);

        await _crossCenterAccess.RecordAsync(
            member.CurrentMedicalCenterId,
            accessReason.Trim(),
            dataType: "Member",
            queryCriteria: JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["memberId"] = memberId.ToString("D")
            }),
            entityType: nameof(Member),
            entityId: memberId.ToString(),
            cancellationToken);

        return details;
    }

    /// <summary>
    /// يسجّل منتسباً في المركز النشط مع توليد FileNumber/QueueNumber داخل معاملة واحدة،
    /// وحساب BMI في الاستجابة، وتحويل سباق تكرار رقم التجنيد إلى استثناء أعمال.
    /// </summary>
    public async Task<MemberDetailsDto> CreateAsync(
        CreateMemberRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.CreateUpdate,
            clientCenterId,
            cancellationToken);

        var userId = RequireUser();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activeRules = await LoadActiveValidationRulesAsync(cancellationToken);
        MemberFieldValidation.ValidateCreateOrUpdate(request, today, activeRules);

        await EnsureReferenceAsync(request.MaritalStatusId, request.RecruitmentCenterId, request.BloodTypeId, cancellationToken);

        var center = await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken);
        MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);

        var recruitmentNumber = request.RecruitmentNumber.Trim();
        if (await _members.RecruitmentNumberExistsAsync(recruitmentNumber, cancellationToken: cancellationToken))
            throw new MemberRecruitmentNumberExistsException(recruitmentNumber);

        await using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var (fileNumber, queueNumber) = await _members.AllocateFileAndQueueNumbersAsync(
                centerId, today, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var memberId = Guid.NewGuid();
            var member = new Member
            {
                MemberId = memberId,
                FileNumber = fileNumber,
                RecruitmentNumber = recruitmentNumber,
                FullName = request.FullName.Trim(),
                MotherName = request.MotherName.Trim(),
                BirthDate = request.BirthDate,
                MaritalStatusId = request.MaritalStatusId,
                Occupation = request.Occupation.Trim(),
                RecruitmentCenterId = request.RecruitmentCenterId,
                BloodTypeId = request.BloodTypeId,
                BloodSugarMgDl = request.BloodSugarMgDl,
                HeightCm = request.HeightCm,
                WeightKg = request.WeightKg,
                BloodPressure = TrimOrNull(request.BloodPressure),
                PulseBpm = request.PulseBpm,
                HasTattoo = request.HasTattoo,
                DistinguishingMarks = TrimOrNull(request.DistinguishingMarks),
                CurrentMedicalCenterId = centerId,
                QueueMedicalCenterId = centerId,
                QueueDate = today,
                QueueNumber = queueNumber,
                ExportStatus = "UNEXPORTED",
                DataRevision = 1,
                CreatedByUserId = userId,
                UpdatedByUserId = userId,
                CreatedAt = now,
                UpdatedAt = now
            };

            _members.Add(member);
            _members.AddIdentity(new MemberCenterIdentity
            {
                MemberCenterIdentityId = Guid.NewGuid(),
                MemberId = memberId,
                MedicalCenterId = centerId,
                CenterMemberId = fileNumber.ToString(),
                ValidFrom = now,
                ValidTo = null,
                AssignedByUserId = userId,
                ClosedByUserId = null
            });

            RecordAudit(
                userId,
                centerId,
                "MemberCreate",
                "SUCCESS",
                memberId.ToString(),
                "تم إنشاء ملف المنتسب",
                oldValues: null,
                newValues: JsonSerializer.Serialize(new
                {
                    member.FileNumber,
                    member.QueueNumber,
                    member.RecruitmentNumber,
                    member.FullName,
                    member.CurrentMedicalCenterId
                }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            var created = await _members.GetByIdInCenterAsync(memberId, centerId, asNoTracking: true, cancellationToken)
                ?? throw new MemberNotFoundException();
            return await ToDetailsAsync(created, cancellationToken);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(cancellationToken);
            // سباق TOCTOU على القيد الفريد لرقم التجنيد بعد الفحص المسبق.
            if (await _members.RecruitmentNumberExistsAsync(recruitmentNumber, cancellationToken: cancellationToken))
                throw new MemberRecruitmentNumberExistsException(recruitmentNumber);
            throw;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// يعدّل بيانات المنتسب الشخصية والطبية ضمن المركز النشط (FR-024)، مع ثبات الأرقام/المركز وإعادة حساب BMI عبر العمود المحسوب.
    /// </summary>
    /// <remarks>
    /// TODO(FR-024 A5): تقييد التعديل عند وجود قرار نهائي معتمد معلّق حتى اعتماد سياسة الجهة / مسار القرار النهائي (T-07).
    /// السلوك المؤقت المعتمد: السماح بالتعديل مع تسجيل Audit بالقيم السابقة والجديدة دون قفل.
    /// </remarks>
    public async Task<MemberDetailsDto> UpdateAsync(
        Guid memberId,
        UpdateMemberRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        // TODO(FR-024 A5): لا يُفحص FinalDecision هنا — السماح + Audit حتى سياسة الجهة / T-07.
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.CreateUpdate,
            clientCenterId,
            cancellationToken);

        var userId = RequireUser();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activeRules = await LoadActiveValidationRulesAsync(cancellationToken);
        MemberFieldValidation.ValidateCreateOrUpdate(request, today, activeRules);

        await EnsureReferenceAsync(request.MaritalStatusId, request.RecruitmentCenterId, request.BloodTypeId, cancellationToken);

        var member = await _members.GetByIdInCenterAsync(memberId, centerId, asNoTracking: false, cancellationToken)
            ?? throw new MemberNotFoundException();

        var recruitmentNumber = request.RecruitmentNumber.Trim();
        if (await _members.RecruitmentNumberExistsAsync(recruitmentNumber, excludingMemberId: memberId, cancellationToken))
            throw new MemberRecruitmentNumberExistsException(recruitmentNumber);

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            member.FullName,
            member.MotherName,
            member.BirthDate,
            member.MaritalStatusId,
            member.Occupation,
            member.RecruitmentNumber,
            member.RecruitmentCenterId,
            member.BloodTypeId,
            member.BloodSugarMgDl,
            member.HeightCm,
            member.WeightKg,
            member.BloodPressure,
            member.PulseBpm,
            member.HasTattoo,
            member.DistinguishingMarks,
            member.FileNumber,
            member.CurrentMedicalCenterId,
            member.DataRevision
        });

        var preservedFile = member.FileNumber;
        var preservedCenter = member.CurrentMedicalCenterId;
        var preservedQueueCenter = member.QueueMedicalCenterId;
        var preservedQueueDate = member.QueueDate;
        var preservedQueueNumber = member.QueueNumber;

        member.FullName = request.FullName.Trim();
        member.MotherName = request.MotherName.Trim();
        member.BirthDate = request.BirthDate;
        member.MaritalStatusId = request.MaritalStatusId;
        member.Occupation = request.Occupation.Trim();
        member.RecruitmentNumber = recruitmentNumber;
        member.RecruitmentCenterId = request.RecruitmentCenterId;
        member.BloodTypeId = request.BloodTypeId;
        member.BloodSugarMgDl = request.BloodSugarMgDl;
        member.HeightCm = request.HeightCm;
        member.WeightKg = request.WeightKg;
        member.BloodPressure = TrimOrNull(request.BloodPressure);
        member.PulseBpm = request.PulseBpm;
        member.HasTattoo = request.HasTattoo;
        member.DistinguishingMarks = TrimOrNull(request.DistinguishingMarks);
        member.FileNumber = preservedFile;
        member.CurrentMedicalCenterId = preservedCenter;
        member.QueueMedicalCenterId = preservedQueueCenter;
        member.QueueDate = preservedQueueDate;
        member.QueueNumber = preservedQueueNumber;
        member.DataRevision += 1;
        member.UpdatedByUserId = userId;
        member.UpdatedAt = DateTimeOffset.UtcNow;

        RecordAudit(
            userId,
            centerId,
            "MemberUpdate",
            "SUCCESS",
            member.MemberId.ToString(),
            "تم تعديل ملف المنتسب",
            oldValues: oldSnapshot,
            newValues: JsonSerializer.Serialize(new
            {
                member.FullName,
                member.MotherName,
                member.BirthDate,
                member.MaritalStatusId,
                member.Occupation,
                member.RecruitmentNumber,
                member.RecruitmentCenterId,
                member.BloodTypeId,
                member.BloodSugarMgDl,
                member.HeightCm,
                member.WeightKg,
                member.BloodPressure,
                member.PulseBpm,
                member.HasTattoo,
                member.DistinguishingMarks,
                member.FileNumber,
                member.CurrentMedicalCenterId,
                member.DataRevision
            }));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var updated = await _members.GetByIdInCenterAsync(memberId, centerId, asNoTracking: true, cancellationToken)
            ?? throw new MemberNotFoundException();
        return await ToDetailsAsync(updated, cancellationToken);
    }

    /// <summary>
    /// يولّد PDF إيصال المنتسب والباركود (FR-027) ضمن المركز النشط دون تعديل رقم الملف أو رقم الدور.
    /// </summary>
    public async Task<ExportFileDto> GetReceiptPdfAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.PrintReceipt,
            clientCenterId,
            cancellationToken);

        var userId = RequireUser();
        var member = await _members.GetByIdInCenterAsync(memberId, centerId, asNoTracking: true, cancellationToken)
            ?? throw new MemberNotFoundException();

        var generatedAt = DateTimeOffset.UtcNow;
        var pdf = MemberReceiptPdfBuilder.Build(member, generatedAt);

        RecordAudit(
            userId,
            centerId,
            "MemberReceiptPrint",
            "SUCCESS",
            member.MemberId.ToString(),
            "تم توليد إيصال المنتسب",
            oldValues: null,
            newValues: JsonSerializer.Serialize(new
            {
                member.FileNumber,
                member.QueueNumber,
                member.FullName,
                member.CurrentMedicalCenterId,
                member.QueueDate,
                GeneratedAt = generatedAt
            }));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return pdf;
    }

    /// <summary>ينزّل قالب استيراد Excel المعتمد (FR-028).</summary>
    public async Task<ExportFileDto> GetImportTemplateAsync(
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.Import,
            clientCenterId,
            cancellationToken);
        _ = cancellationToken;

        var bytes = MemberImportExcel.BuildTemplateBytes();
        return new ExportFileDto
        {
            FileContents = bytes,
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileName = "قالب_استيراد_المنتسبين.xlsx"
        };
    }

    /// <summary>يرفع ملف Excel ويعيد معاينة دون إنشاء منتسبين (FR-028 / BR-076).</summary>
    public async Task<MemberImportPreviewResult> PreviewImportAsync(
        IFormFile file,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.Import,
            clientCenterId,
            cancellationToken);

        var userId = RequireUser();
        if (file is null)
            throw new MemberImportException("ملف الاستيراد مطلوب.");

        await using var stream = file.OpenReadStream();
        MemberImportExcel.ValidateUpload(file.FileName, file.Length, stream);

        var contentHash = MemberImportExcel.ComputeSha256Hex(stream);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activeRules = await LoadActiveValidationRulesAsync(cancellationToken);

        var parsed = MemberImportExcel.ParseAndValidate(stream, today, activeRules);

        var valid = new List<MemberImportValidRow>();
        var issues = parsed.Issues.ToList();

        foreach (var row in parsed.ValidRows)
        {
            try
            {
                await EnsureReferenceAsync(
                    row.MaritalStatusId,
                    row.RecruitmentCenterId,
                    row.BloodTypeId,
                    cancellationToken);

                if (await _members.RecruitmentNumberExistsAsync(
                        row.RecruitmentNumber.Trim(),
                        cancellationToken: cancellationToken))
                {
                    issues.Add(CreateImportIssue(
                        row.RowNumber,
                        "Duplicate",
                        new[] { "رقم المنتسب/التجنيد مسجّل مسبقاً في النظام." },
                        row.FullName,
                        row.RecruitmentNumber.Trim()));
                    continue;
                }

                valid.Add(row);
            }
            catch (InvalidMemberDataException ex)
            {
                issues.Add(CreateImportIssue(
                    row.RowNumber,
                    "Invalid",
                    new[] { ex.Message },
                    row.FullName,
                    row.RecruitmentNumber));
            }
        }

        await EnrichRecruitmentCenterNamesAsync(valid, cancellationToken);

        var duplicateCount = issues.Count(i => i.Status == "Duplicate");
        var invalidCount = issues.Count(i => i.Status == "Invalid");
        var importId = Guid.NewGuid();
        var batch = new MemberImportBatch
        {
            ImportBatchId = importId,
            CenterId = centerId,
            CreatedByUserId = userId,
            Status = MemberImportBatchStatuses.Previewed,
            FileName = Path.GetFileName(file.FileName),
            ContentHash = contentHash,
            ValidCount = valid.Count,
            DuplicateCount = duplicateCount,
            InvalidCount = invalidCount,
            ValidRowsJson = JsonSerializer.Serialize(valid, ImportJsonOptions),
            ErrorsJson = JsonSerializer.Serialize(issues, ImportJsonOptions),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _importBatches.Add(batch);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new MemberImportPreviewResult
        {
            ImportId = importId,
            TotalRows = valid.Count + issues.Count,
            ValidCount = valid.Count,
            DuplicateCount = duplicateCount,
            InvalidCount = invalidCount,
            ValidRows = valid,
            Issues = issues
        };
    }

    /// <summary>يعتمد السجلات الصالحة مرة واحدة فقط ويولّد أرقام الملف/الدور في المركز النشط.</summary>
    public async Task<MemberImportCommitResult> CommitImportAsync(
        Guid importId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.Import,
            clientCenterId,
            cancellationToken);

        var userId = RequireUser();
        var batch = await _importBatches.GetByIdInCenterAsync(importId, centerId, asNoTracking: false, cancellationToken)
            ?? throw new MemberImportBatchNotFoundException();

        if (batch.Status == MemberImportBatchStatuses.Committed)
            throw new MemberImportAlreadyCommittedException();
        if (batch.Status != MemberImportBatchStatuses.Previewed)
            throw new MemberImportNotPreviewedException();

        var center = await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken);
        MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);

        var rows = JsonSerializer.Deserialize<List<MemberImportValidRow>>(batch.ValidRowsJson, ImportJsonOptions)
            ?? new List<MemberImportValidRow>();
        if (rows.Count == 0)
            throw new MemberImportException("لا توجد سجلات صالحة للاعتماد في هذه الجلسة.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var createdFileNumbers = new List<long>();
        var skippedDuplicate = 0;

        await using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // إعادة التحقق من الحالة داخل المعاملة لمنع الاعتماد المزدوج المتزامن.
            if (batch.Status != MemberImportBatchStatuses.Previewed)
                throw new MemberImportAlreadyCommittedException();

            foreach (var row in rows)
            {
                var recruitmentNumber = row.RecruitmentNumber.Trim();
                if (await _members.RecruitmentNumberExistsAsync(recruitmentNumber, cancellationToken: cancellationToken))
                {
                    skippedDuplicate++;
                    continue;
                }

                var (fileNumber, queueNumber) = await _members.AllocateFileAndQueueNumbersAsync(
                    centerId, today, cancellationToken);

                var now = DateTimeOffset.UtcNow;
                var memberId = Guid.NewGuid();
                var member = new Member
                {
                    MemberId = memberId,
                    FileNumber = fileNumber,
                    RecruitmentNumber = recruitmentNumber,
                    FullName = row.FullName.Trim(),
                    MotherName = row.MotherName.Trim(),
                    BirthDate = row.BirthDate,
                    MaritalStatusId = row.MaritalStatusId,
                    Occupation = row.Occupation.Trim(),
                    RecruitmentCenterId = row.RecruitmentCenterId,
                    BloodTypeId = row.BloodTypeId,
                    BloodSugarMgDl = row.BloodSugarMgDl,
                    HeightCm = row.HeightCm,
                    WeightKg = row.WeightKg,
                    BloodPressure = TrimOrNull(row.BloodPressure),
                    PulseBpm = row.PulseBpm,
                    HasTattoo = row.HasTattoo,
                    DistinguishingMarks = TrimOrNull(row.DistinguishingMarks),
                    CurrentMedicalCenterId = centerId,
                    QueueMedicalCenterId = centerId,
                    QueueDate = today,
                    QueueNumber = queueNumber,
                    ExportStatus = "UNEXPORTED",
                    DataRevision = 1,
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _members.Add(member);
                _members.AddIdentity(new MemberCenterIdentity
                {
                    MemberCenterIdentityId = Guid.NewGuid(),
                    MemberId = memberId,
                    MedicalCenterId = centerId,
                    CenterMemberId = fileNumber.ToString(),
                    ValidFrom = now,
                    ValidTo = null,
                    AssignedByUserId = userId,
                    ClosedByUserId = null
                });

                createdFileNumbers.Add(fileNumber);
            }

            batch.Status = MemberImportBatchStatuses.Committed;
            batch.CommittedAt = DateTimeOffset.UtcNow;

            RecordAudit(
                userId,
                centerId,
                "MemberImport",
                "SUCCESS",
                batch.ImportBatchId.ToString(),
                "تم اعتماد استيراد منتسبين من Excel",
                oldValues: null,
                newValues: JsonSerializer.Serialize(new
                {
                    batch.ImportBatchId,
                    batch.FileName,
                    CommittedCount = createdFileNumbers.Count,
                    SkippedDuplicate = skippedDuplicate,
                    batch.InvalidCount,
                    batch.DuplicateCount
                }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(cancellationToken);
            throw new MemberImportException("تعذر اعتماد الاستيراد بسبب تعارض بيانات. أعد المعاينة ثم حاول مجدداً.");
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return new MemberImportCommitResult
        {
            ImportId = importId,
            CommittedCount = createdFileNumbers.Count,
            SkippedDuplicateCount = skippedDuplicate,
            SkippedInvalidCount = batch.InvalidCount,
            CreatedFileNumbers = createdFileNumbers
        };
    }

    /// <summary>يلغي جلسة المعاينة دون حفظ منتسبين.</summary>
    public async Task CancelImportAsync(
        Guid importId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.Import,
            clientCenterId,
            cancellationToken);

        var batch = await _importBatches.GetByIdInCenterAsync(importId, centerId, asNoTracking: false, cancellationToken)
            ?? throw new MemberImportBatchNotFoundException();

        if (batch.Status == MemberImportBatchStatuses.Committed)
            throw new MemberImportAlreadyCommittedException();
        if (batch.Status != MemberImportBatchStatuses.Previewed)
            throw new MemberImportNotPreviewedException();

        batch.Status = MemberImportBatchStatuses.Cancelled;
        batch.CancelledAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>ينزّل تقرير أخطاء جلسة الاستيراد كملف Excel.</summary>
    public async Task<ExportFileDto> GetImportErrorsAsync(
        Guid importId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.Import,
            clientCenterId,
            cancellationToken);

        var batch = await _importBatches.GetByIdInCenterAsync(importId, centerId, asNoTracking: true, cancellationToken)
            ?? throw new MemberImportBatchNotFoundException();

        var issues = JsonSerializer.Deserialize<List<MemberImportIssueRow>>(batch.ErrorsJson, ImportJsonOptions)
            ?? new List<MemberImportIssueRow>();

        return await _export.ExportToExcelAsync(issues, new ExportOptions
        {
            ReportName = "اخطاء_استيراد_المنتسبين",
            SheetName = "الأخطاء",
            CustomTitle = "تقرير أخطاء استيراد المنتسبين",
            IncludeSummary = false,
            ColumnMappings = new Dictionary<string, string>
            {
                [nameof(MemberImportIssueRow.RowNumber)] = "رقم الصف",
                [nameof(MemberImportIssueRow.Status)] = "الحالة",
                [nameof(MemberImportIssueRow.FullName)] = "الاسم الكامل",
                [nameof(MemberImportIssueRow.RecruitmentNumber)] = "رقم المنتسب/التجنيد",
                [nameof(MemberImportIssueRow.Reason)] = "سبب الخطأ"
            }
        });
    }

    /// <summary>يصدّر قائمة المنتسبين وفق تصفية القائمة ونطاق المركز النشط (FR-029).</summary>
    public async Task<ExportFileDto> ExportAsync(
        MemberListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.Export,
            clientCenterId,
            cancellationToken);

        var userId = RequireUser();
        MemberFieldValidation.ApplyBarcodeToFileNumber(query);
        MemberFieldValidation.ValidateListDateFilter(query.Date, query.FromDate, query.ToDate);

        var q = BuildFilteredMemberQuery(centerId, query);
        var totalCount = await q.CountAsync(cancellationToken);
        if (totalCount == 0)
            throw new MemberExportEmptyException();
        if (totalCount > MaxExportRows)
            throw new MemberImportException(
                $"عدد السجلات ({totalCount}) يتجاوز حد التصدير ({MaxExportRows}). ضيّق التصفية.",
                "MEMBER_EXPORT_TOO_LARGE");

        var members = await q.Take(MaxExportRows).ToListAsync(cancellationToken);
        var center = await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken);
        var centerName = center?.NameAr;
        var categories = await LoadBmiCategoriesAsync(cancellationToken);

        var rows = members.Select(m =>
        {
            var bmi = m.Bmi ?? MemberBmi.Calculate(m.HeightCm, m.WeightKg);
            return new MemberExportRowDto
            {
                FileNumber = m.FileNumber,
                QueueNumber = m.QueueNumber,
                QueueDate = m.QueueDate,
                FullName = m.FullName,
                RecruitmentNumber = m.RecruitmentNumber,
                CreatedAt = m.CreatedAt,
                CurrentMedicalCenterId = m.CurrentMedicalCenterId,
                CurrentMedicalCenterNameAr = centerName,
                Bmi = bmi,
                BmiCategoryNameAr = MemberBmi.Classify(bmi, categories)
            };
        }).ToList();

        var file = await _export.ExportToExcelAsync(rows, new ExportOptions
        {
            ReportName = "منتسبين",
            SheetName = "المنتسبون",
            CustomTitle = "تصدير قائمة المنتسبين",
            BranchName = centerName,
            IncludeSummary = false,
            ColumnMappings = new Dictionary<string, string>
            {
                [nameof(MemberExportRowDto.FileNumber)] = "رقم الملف",
                [nameof(MemberExportRowDto.QueueNumber)] = "رقم الدور",
                [nameof(MemberExportRowDto.QueueDate)] = "تاريخ الدور",
                [nameof(MemberExportRowDto.FullName)] = "الاسم الكامل",
                [nameof(MemberExportRowDto.RecruitmentNumber)] = "رقم المنتسب/التجنيد",
                [nameof(MemberExportRowDto.CreatedAt)] = "تاريخ التسجيل",
                [nameof(MemberExportRowDto.CurrentMedicalCenterId)] = "معرف المركز",
                [nameof(MemberExportRowDto.CurrentMedicalCenterNameAr)] = "اسم المركز",
                [nameof(MemberExportRowDto.Bmi)] = "مؤشر كتلة الجسم",
                [nameof(MemberExportRowDto.BmiCategoryNameAr)] = "تصنيف مؤشر كتلة الجسم"
            }
        });

        RecordAudit(
            userId,
            centerId,
            "MemberExport",
            "SUCCESS",
            centerId.ToString(),
            "تم تصدير قائمة المنتسبين إلى Excel",
            oldValues: null,
            newValues: JsonSerializer.Serialize(new
            {
                RecordCount = rows.Count,
                query.FileNumber,
                query.RecruitmentNumber,
                query.FullName,
                query.Date,
                query.FromDate,
                query.ToDate
            }));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return file;
    }

    #region Private helpers

    private async Task<PaginatedList<MemberListItemDto>> ExecuteListAsync(
        MemberListQuery query,
        Guid? centerId,
        CancellationToken cancellationToken)
    {
        MemberFieldValidation.ApplyBarcodeToFileNumber(query);
        MemberFieldValidation.ValidateListDateFilter(query.Date, query.FromDate, query.ToDate);

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize < 1 ? 10 : query.PageSize;
        if (pageSize > 500) pageSize = 500;

        var q = BuildFilteredMemberQuery(centerId, query);
        var totalCount = await q.CountAsync(cancellationToken);
        var page = await q
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var categories = await LoadBmiCategoriesAsync(cancellationToken);
        var items = page.Select(m =>
        {
            var dto = _mapper.Map<MemberListItemDto>(m);
            var bmi = m.Bmi ?? MemberBmi.Calculate(m.HeightCm, m.WeightKg);
            dto.Bmi = bmi;
            dto.BmiCategoryNameAr = MemberBmi.Classify(bmi, categories);
            return dto;
        }).ToList();

        return new PaginatedList<MemberListItemDto>(items, totalCount, pageNumber, pageSize);
    }

    /// <summary>
    /// يبني استعلام القائمة بعد تطبيق نطاق المركز (إن وُجد) ثم معايير البحث والتصفية الزمنية.
    /// </summary>
    private IQueryable<Member> BuildFilteredMemberQuery(Guid? centerId, MemberListQuery query)
    {
        var q = _members.Query();
        if (centerId.HasValue)
            q = q.Where(m => m.CurrentMedicalCenterId == centerId.Value);

        if (query.FileNumber.HasValue)
            q = q.Where(m => m.FileNumber == query.FileNumber.Value);

        if (query.QueueNumber.HasValue)
            q = q.Where(m => m.QueueNumber == query.QueueNumber.Value);

        if (!string.IsNullOrWhiteSpace(query.RecruitmentNumber))
        {
            var term = query.RecruitmentNumber.Trim().ToLower();
            q = q.Where(m => m.RecruitmentNumber.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.FullName))
        {
            var term = query.FullName.Trim().ToLower();
            q = q.Where(m => m.FullName.ToLower().Contains(term));
        }

        q = ApplyCreatedAtFilter(q, query.Date, query.FromDate, query.ToDate);
        return q.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.FileNumber);
    }

    private async Task<MemberDetailsDto> ToDetailsAsync(Member member, CancellationToken cancellationToken)
    {
        var dto = _mapper.Map<MemberDetailsDto>(member);
        var bmi = member.Bmi ?? MemberBmi.Calculate(member.HeightCm, member.WeightKg);
        dto.Bmi = bmi;
        dto.BmiCategoryNameAr = MemberBmi.Classify(bmi, await LoadBmiCategoriesAsync(cancellationToken));

        var notes = await _members.GetActiveNotesAsync(member.MemberId, cancellationToken);
        dto.GeneralNotes = notes
            .Where(n => !string.IsNullOrWhiteSpace(n.NoteText))
            .Select(n => new MemberGeneralNoteItemDto
            {
                NoteId = n.NoteId,
                NoteText = n.NoteText,
                AuthorUserId = n.AuthorUserId,
                AuthorUserName = n.AuthorUser?.UserName,
                AuthorClinicId = n.AuthorClinicId,
                AuthorClinicNameAr = n.AuthorClinic?.NameAr,
                WrittenAt = n.WrittenAt
            })
            .ToList();
        dto.HasGeneralNotesAlert = dto.GeneralNotes.Count > 0;

        var transfer = await _members.GetLastTransferAsync(member.MemberId, cancellationToken);
        if (transfer is not null)
        {
            dto.LastTransfer = new MemberTransferSummaryDto
            {
                TransferId = transfer.TransferId,
                FromCenterId = transfer.FromCenterId,
                FromCenterNameAr = transfer.FromCenter?.NameAr,
                ToCenterId = transfer.ToCenterId,
                ToCenterNameAr = transfer.ToCenter?.NameAr,
                TransferReason = transfer.TransferReason,
                ExecutedAt = transfer.ExecutedAt
            };
        }

        return dto;
    }

    /// <summary>
    /// يبني معايير البحث كـ JSON صالح لعمود jsonb في CrossCenterAccess.QueryCriteria.
    /// </summary>
    private static string BuildListCriteria(MemberListQuery query, Guid? targetCenterId)
    {
        var criteria = new Dictionary<string, object?>();
        if (targetCenterId.HasValue) criteria["centerId"] = targetCenterId.Value.ToString("D");
        if (query.FileNumber.HasValue) criteria["fileNumber"] = query.FileNumber.Value;
        if (!string.IsNullOrWhiteSpace(query.Barcode)) criteria["barcode"] = query.Barcode.Trim();
        if (!string.IsNullOrWhiteSpace(query.FullName)) criteria["fullName"] = query.FullName.Trim();
        if (!string.IsNullOrWhiteSpace(query.RecruitmentNumber))
            criteria["recruitmentNumber"] = query.RecruitmentNumber.Trim();
        if (query.QueueNumber.HasValue) criteria["queueNumber"] = query.QueueNumber.Value;
        if (query.Date.HasValue) criteria["date"] = query.Date.Value.ToString("yyyy-MM-dd");
        if (query.FromDate.HasValue) criteria["fromDate"] = query.FromDate.Value.ToString("yyyy-MM-dd");
        if (query.ToDate.HasValue) criteria["toDate"] = query.ToDate.Value.ToString("yyyy-MM-dd");
        criteria["pageNumber"] = query.PageNumber;
        criteria["pageSize"] = query.PageSize;
        return JsonSerializer.Serialize(criteria);
    }

    private async Task EnrichRecruitmentCenterNamesAsync(
        IReadOnlyList<MemberImportValidRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return;

        var ids = rows.Select(r => r.RecruitmentCenterId).Distinct().ToList();
        var names = await _references.RecruitmentCenters()
            .Where(c => ids.Contains(c.RecruitmentCenterId))
            .Select(c => new { c.RecruitmentCenterId, c.NameAr })
            .ToDictionaryAsync(c => c.RecruitmentCenterId, c => c.NameAr, cancellationToken);

        foreach (var row in rows)
        {
            if (names.TryGetValue(row.RecruitmentCenterId, out var nameAr))
                row.RecruitmentCenterNameAr = nameAr;
        }
    }

    private static MemberImportIssueRow CreateImportIssue(
        int rowNumber,
        string status,
        IReadOnlyList<string> errors,
        string? fullName,
        string? recruitmentNumber)
    {
        var list = errors.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToList();
        if (list.Count == 0)
            list.Add("سجل غير صالح.");

        return new MemberImportIssueRow
        {
            RowNumber = rowNumber,
            Status = status,
            Errors = list,
            Reason = string.Join("؛ ", list),
            FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim(),
            RecruitmentNumber = string.IsNullOrWhiteSpace(recruitmentNumber) ? null : recruitmentNumber.Trim()
        };
    }

    private async Task EnsureReferenceAsync(
        short maritalStatusId,
        Guid recruitmentCenterId,
        short? bloodTypeId,
        CancellationToken cancellationToken)
    {
        var maritalOk = await _references.MaritalStatuses()
            .AnyAsync(x => x.MaritalStatusId == maritalStatusId && x.IsActive, cancellationToken);
        if (!maritalOk)
            throw new InvalidMemberDataException("الحالة الاجتماعية غير موجودة أو غير فعّالة.");

        var recruitmentOk = await _references.RecruitmentCenters()
            .AnyAsync(x => x.RecruitmentCenterId == recruitmentCenterId && x.IsActive, cancellationToken);
        if (!recruitmentOk)
            throw new InvalidMemberDataException("مركز التجنيد غير موجود أو غير فعّال.");

        if (bloodTypeId.HasValue)
        {
            var bloodOk = await _references.BloodTypes()
                .AnyAsync(x => x.BloodTypeId == bloodTypeId.Value && x.IsActive, cancellationToken);
            if (!bloodOk)
                throw new InvalidMemberDataException("زمرة الدم غير موجودة أو غير فعّالة.");
        }
    }

    private async Task<List<ValidationRule>> LoadActiveValidationRulesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return await _references.ValidationRules()
            .Where(r => r.IsActive
                && r.TargetEntity.ToLower() == "member"
                && r.ValidFrom <= now
                && (r.ValidTo == null || r.ValidTo >= now))
            .ToListAsync(cancellationToken);
    }

    private Task<List<BmiCategory>> LoadBmiCategoriesAsync(CancellationToken cancellationToken)
        => _references.BmiCategories().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);

    private static IQueryable<Member> ApplyCreatedAtFilter(
        IQueryable<Member> query,
        DateOnly? date,
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        if (date.HasValue)
        {
            var start = new DateTimeOffset(date.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = start.AddDays(1);
            return query.Where(m => m.CreatedAt >= start && m.CreatedAt < end);
        }

        if (fromDate.HasValue)
        {
            var start = new DateTimeOffset(fromDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(m => m.CreatedAt >= start);
        }

        if (toDate.HasValue)
        {
            var endExclusive = new DateTimeOffset(toDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(1);
            query = query.Where(m => m.CreatedAt < endExclusive);
        }

        return query;
    }

    private void RecordAudit(
        string userId,
        Guid centerId,
        string eventType,
        string result,
        string entityId,
        string reasonText,
        string? oldValues,
        string? newValues)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = eventType,
            Result = result,
            EntityType = nameof(Member),
            EntityId = entityId,
            ReasonText = reasonText,
            OldValues = oldValues,
            NewValues = newValues,
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });
    }

    private string RequireUser()
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId) || _currentUser.UserId == "0")
            throw new UnauthenticatedUserException();
        return _currentUser.UserId;
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    #endregion
}
