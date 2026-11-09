using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Common.Permissions;
using Application.Common.Results;
using Application.Common.Security;
using Application.Dtos.Export;
using Application.Dtos.ExportDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.Time;
using Application.Services.Supervisor;
using Domain.Entities.Audit;
using Domain.Entities.Core;
using Domain.Entities.Export;
using Domain.Entities.Patient;
using Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Export;

/// <summary>
/// دورة تصدير التجنيد: قائمة الانتظار، إنشاء الدفعة، توليد XLSX عبر FileAsset،
/// السجل، وإعادة التصدير بعد تعديل القرار (FR-058…FR-061 / CON-08).
/// </summary>
public class RecruitmentExportService : IRecruitmentExportService
{
    public const string StatusGenerating = "GENERATING";
    public const string StatusCompleted = "COMPLETED";
    public const string StatusFailed = "FAILED";

    public const string BatchCreatedEventType = "RecruitmentExportBatchCreated";
    public const string BatchCompletedEventType = "RecruitmentExportBatchCompleted";
    public const string BatchFailedEventType = "RecruitmentExportBatchFailed";
    public const string BatchDownloadedEventType = "RecruitmentExportBatchDownloaded";
    public const string ReexportEventType = "RecruitmentExportReexport";
    public const string HistoryExportedEventType = "RecruitmentExportHistoryExported";

    private const string StorageEntityType = "RecruitmentExportBatch";
    private const string ExcelMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string DownloadPathFormat = "/api/export/recruitment/batches/{0}/download";
    private const int MaxPageSize = 200;
    private const int MaxHistoryExportRows = 10_000;
    private const int MaxBatchMembers = 5_000;

    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;
    private readonly IMemberRepository _members;
    private readonly IClinicWorkspaceRepository _clinicWorkspace;
    private readonly IMedicalCenterRepository _centers;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IRecruitmentExportRepository _exports;
    private readonly IAttachmentRepository _attachments;
    private readonly IExportService _export;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;

    public RecruitmentExportService(
        IPermissionGate permissionGate,
        ICurrentUserService currentUser,
        IMemberRepository members,
        IClinicWorkspaceRepository clinicWorkspace,
        IMedicalCenterRepository centers,
        IReferenceDataRepository referenceData,
        IRecruitmentExportRepository exports,
        IAttachmentRepository attachments,
        IExportService export,
        IFileStorageService fileStorage,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        IBusinessClock? businessClock = null)
    {
        _permissionGate = permissionGate;
        _currentUser = currentUser;
        _members = members;
        _clinicWorkspace = clinicWorkspace;
        _centers = centers;
        _referenceData = referenceData;
        _exports = exports;
        _attachments = attachments;
        _export = export;
        _fileStorage = fileStorage;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = businessClock ?? BusinessClock.Utc;
    }

    #region Public Operations

    /// <inheritdoc />
    public async Task<PaginatedList<RecruitmentExportPendingItemDto>> ListPendingAsync(
        RecruitmentExportPendingQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new RecruitmentExportPendingQuery();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Export.Recruitment,
            clientCenterId,
            cancellationToken);

        NormalizePendingQuery(query);

        var rows = await BuildPendingRowsAsync(centerId, query, cancellationToken);
        var total = rows.Count;
        var page = rows
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new PaginatedList<RecruitmentExportPendingItemDto>(page, total, query.PageNumber, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<RecruitmentExportBatchResultDto> CreateBatchAsync(
        CreateRecruitmentExportBatchRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        request ??= new CreateRecruitmentExportBatchRequest();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Export.Recruitment,
            clientCenterId,
            cancellationToken);
        var userId = RequireUserId();

        var center = await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken)
            ?? throw new InvalidRecruitmentExportDataException("المركز النشط غير موجود.");

        var (selectedIds, exclusions) = await ResolveMemberIdsAsync(centerId, request, cancellationToken);
        if (selectedIds.Count == 0)
            throw new RecruitmentExportEmptyException();

        if (selectedIds.Count > MaxBatchMembers)
        {
            throw new InvalidRecruitmentExportDataException(
                $"عدد السجلات ({selectedIds.Count}) يتجاوز حد الدفعة ({MaxBatchMembers}). قسّم التصدير إلى دفعات.",
                new { selectedIds.Count, MaxBatchMembers });
        }

        var eligible = await LoadEligibleAsync(centerId, selectedIds, exclusions, cancellationToken);

        if (eligible.Count == 0)
            throw new RecruitmentExportEmptyException("لا توجد ملفات مؤهلة بعد التحقق النهائي.");

        var resultStatuses = await _referenceData.ResultStatuses(asNoTracking: true)
            .ToDictionaryAsync(r => r.StatusId, cancellationToken);

        var batchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var filterSnapshot = JsonSerializer.Serialize(new
        {
            request.ExportAllPending,
            request.MemberIds,
            request.ResultStatusId,
            request.DecisionFrom,
            request.DecisionTo,
            request.FileNumber,
            request.RecruitmentNumber,
            request.FullName,
            Exclusions = exclusions
        });

        await using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var batch = new RecruitmentExportBatch
        {
            ExportBatchId = batchId,
            CenterId = centerId,
            CenterCodeSnapshot = center.Code,
            CenterNameSnapshot = center.NameAr,
            ExportedByUserId = userId,
            RecordCount = eligible.Count,
            Status = StatusGenerating,
            FilterSnapshot = filterSnapshot,
            CreatedAt = now
        };
        _exports.AddBatch(batch);

        var excelRows = new List<RecruitmentExportExcelRowDto>(eligible.Count);
        var reexportMemberIds = new List<Guid>();

        foreach (var row in eligible)
        {
            var previousBatchId = await _exports.GetLastCompletedBatchIdForMemberAsync(
                row.Member.MemberId,
                cancellationToken);
            var isReexport = previousBatchId.HasValue;
            if (isReexport)
                reexportMemberIds.Add(row.Member.MemberId);

            var reason = row.Decision.ResultStatusId == FinalDecisionService.AcceptedResultStatusId
                ? null
                : row.Decision.RejectionReason;

            _exports.AddItem(new RecruitmentExportItem
            {
                ExportBatchId = batchId,
                MemberId = row.Member.MemberId,
                FinalDecisionId = row.Decision.FinalDecisionId,
                DecisionAtSnapshot = row.Decision.DecidedAt,
                ResultStatusId = row.Decision.ResultStatusId,
                ResultReasonSnapshot = reason,
                FileNumberSnapshot = row.Member.FileNumber,
                RecruitmentNumberSnapshot = row.Member.RecruitmentNumber,
                DataRevisionSnapshot = row.Member.DataRevision,
                IsReexport = isReexport,
                PreviousExportBatchId = previousBatchId,
                ExportedAt = now,
                RecruitmentCenterId = row.Member.RecruitmentCenterId,
                RecruitmentCenterCodeSnapshot = row.RecruitmentCenter.Code,
                RecruitmentCenterNameSnapshot = row.RecruitmentCenter.NameAr
            });

            var resultName = resultStatuses.TryGetValue(row.Decision.ResultStatusId, out var rs)
                ? rs.NameAr
                : row.Decision.ResultStatusId.ToString();

            excelRows.Add(new RecruitmentExportExcelRowDto
            {
                CenterCode = center.Code,
                CenterName = center.NameAr,
                FileNumber = row.Member.FileNumber,
                RecruitmentNumber = row.Member.RecruitmentNumber,
                FullName = row.Member.FullName,
                RecruitmentCenterCode = row.RecruitmentCenter.Code,
                RecruitmentCenterName = row.RecruitmentCenter.NameAr,
                ResultNameAr = resultName,
                ResultReason = reason,
                DecisionAt = row.Decision.DecidedAt
            });
        }

        RecordAudit(
            userId,
            centerId,
            BatchCreatedEventType,
            "SUCCESS",
            nameof(RecruitmentExportBatch),
            batchId.ToString(),
            $"إنشاء دفعة تصدير تجنيد بعدد {eligible.Count}",
            oldValues: null,
            newValues: JsonSerializer.Serialize(new
            {
                batchId,
                eligible.Count,
                ExcludedCount = exclusions.Count,
                ReexportCount = reexportMemberIds.Count
            }));

        if (reexportMemberIds.Count > 0)
        {
            RecordAudit(
                userId,
                centerId,
                ReexportEventType,
                "SUCCESS",
                nameof(RecruitmentExportBatch),
                batchId.ToString(),
                "إعادة تصدير بعد تعديل قرار نهائي",
                oldValues: null,
                newValues: JsonSerializer.Serialize(new { batchId, MemberIds = reexportMemberIds }));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        string? storedPath = null;
        try
        {
            var file = await _export.ExportToExcelAsync(excelRows, new ExportOptions
            {
                ReportName = $"تصدير-تجنيد-{center.Code}",
                SheetName = "التصدير للتجنيد",
                CustomTitle = "تصدير ملفات المنتسبين للتجنيد",
                BranchName = center.NameAr,
                IncludeSummary = false,
                ColumnMappings = new Dictionary<string, string>
                {
                    [nameof(RecruitmentExportExcelRowDto.CenterCode)] = "رمز المركز الطبي",
                    [nameof(RecruitmentExportExcelRowDto.CenterName)] = "اسم المركز الطبي",
                    [nameof(RecruitmentExportExcelRowDto.FileNumber)] = "رقم الملف",
                    [nameof(RecruitmentExportExcelRowDto.RecruitmentNumber)] = "رقم التجنيد",
                    [nameof(RecruitmentExportExcelRowDto.FullName)] = "اسم المنتسب",
                    [nameof(RecruitmentExportExcelRowDto.RecruitmentCenterCode)] = "رمز مركز التجنيد",
                    [nameof(RecruitmentExportExcelRowDto.RecruitmentCenterName)] = "مركز التجنيد",
                    [nameof(RecruitmentExportExcelRowDto.ResultNameAr)] = "النتيجة",
                    [nameof(RecruitmentExportExcelRowDto.ResultReason)] = "السبب",
                    [nameof(RecruitmentExportExcelRowDto.DecisionAt)] = "تاريخ التقييم"
                }
            });

            var formFile = CreateFormFile(file.FileContents, file.FileName, file.ContentType);
            var (path, size) = await _fileStorage.SaveFileAsync(formFile, StorageEntityType, batchId.ToString());
            storedPath = path;

            var fileAssetId = Guid.NewGuid();
            var completedAt = DateTimeOffset.UtcNow;
            _exports.AddFileAsset(new FileAsset
            {
                FileAssetId = fileAssetId,
                CenterId = centerId,
                StorageKey = path,
                OriginalName = SanitizeFileName(file.FileName),
                MimeType = string.IsNullOrWhiteSpace(file.ContentType) ? ExcelMime : file.ContentType,
                Extension = ".xlsx",
                SizeBytes = size,
                Sha256 = ComputeSha256(file.FileContents),
                UploadedByUserId = userId,
                CreatedAt = completedAt,
                IsDeleted = false
            });

            // إعادة تحميل الدفعة والمنتسبين للتتبع داخل المعاملة.
            var trackedBatch = await _exports.GetBatchByIdInCenterAsync(
                batchId, centerId, asNoTracking: false, cancellationToken)
                ?? throw new RecruitmentExportBatchNotFoundException();

            trackedBatch.GeneratedFileId = fileAssetId;
            trackedBatch.Status = StatusCompleted;
            trackedBatch.CompletedAt = completedAt;

            var memberIds = eligible.Select(e => e.Member.MemberId).ToList();

            RecordAudit(
                userId,
                centerId,
                BatchCompletedEventType,
                "SUCCESS",
                nameof(RecruitmentExportBatch),
                batchId.ToString(),
                "اكتمال توليد ملف تصدير التجنيد وتعليم المنتسبين",
                oldValues: null,
                newValues: JsonSerializer.Serialize(new
                {
                    batchId,
                    fileAssetId,
                    RecordCount = memberIds.Count,
                    // لا يُسجَّل StorageKey ولا محتوى الملف.
                    OriginalName = SanitizeFileName(file.FileName),
                    SizeBytes = size
                }));

            // أولاً احفظ الدفعة/الملف/التدقيق، ثم علّم التصدير بأعمدة التصدير فقط
            // حتى لا يطلق trgMemberRevisionOnMaterialUpdate زيادة dataRevision ويعيد UNEXPORTED.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var marked = await _members.MarkExportedForRecruitmentAsync(
                centerId,
                memberIds,
                userId,
                completedAt,
                cancellationToken);
            if (marked != memberIds.Count)
            {
                throw new InvalidRecruitmentExportDataException(
                    "تعذّر تعليم بعض المنتسبين كمصدَّرين بعد توليد الملف.",
                    new { Expected = memberIds.Count, Marked = marked });
            }

            await tx.CommitAsync(cancellationToken);

            return new RecruitmentExportBatchResultDto
            {
                ExportBatchId = batchId,
                Status = StatusCompleted,
                RecordCount = memberIds.Count,
                ExcludedCount = exclusions.Count,
                Exclusions = exclusions,
                GeneratedFileId = fileAssetId,
                CreatedAt = now,
                CompletedAt = completedAt,
                DownloadPath = string.Format(DownloadPathFormat, batchId)
            };
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            if (!string.IsNullOrWhiteSpace(storedPath))
                await _fileStorage.DeleteFileAsync(storedPath);

            try
            {
                var failedBatch = await _exports.GetBatchByIdInCenterAsync(
                    batchId, centerId, asNoTracking: false, cancellationToken);
                if (failedBatch is not null && failedBatch.Status == StatusGenerating)
                {
                    failedBatch.Status = StatusFailed;
                    failedBatch.CompletedAt = DateTimeOffset.UtcNow;
                    failedBatch.GeneratedFileId = null;
                }

                RecordAudit(
                    userId,
                    centerId,
                    BatchFailedEventType,
                    "FAILURE",
                    nameof(RecruitmentExportBatch),
                    batchId.ToString(),
                    "فشل توليد ملف تصدير التجنيد دون تعليم المنتسبين",
                    oldValues: null,
                    newValues: JsonSerializer.Serialize(new
                    {
                        batchId,
                        ErrorType = ex.GetType().Name
                    }));

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
            }

            throw new RecruitmentExportGenerationFailedException(batchId, ex.Message);
        }
        catch (BusinessException)
        {
            if (!string.IsNullOrWhiteSpace(storedPath))
                await _fileStorage.DeleteFileAsync(storedPath);
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PaginatedList<RecruitmentExportBatchSummaryDto>> ListBatchesAsync(
        RecruitmentExportBatchQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new RecruitmentExportBatchQuery();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Export.ViewHistory,
            clientCenterId,
            cancellationToken);

        NormalizePaging(query);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim().ToUpperInvariant();
            if (status is not StatusGenerating and not StatusCompleted and not StatusFailed)
                throw new InvalidRecruitmentExportDataException("حالة الدفعة غير صالحة.");
            query.Status = status;
        }

        var q = _exports.QueryBatches(asNoTracking: true)
            .Where(b => b.CenterId == centerId);

        if (!string.IsNullOrWhiteSpace(query.Status))
            q = q.Where(b => b.Status == query.Status);

        if (query.FromDate.HasValue)
        {
            var fromOffset = _clock.StartOfDay(query.FromDate.Value);
            q = q.Where(b => b.CreatedAt >= fromOffset);
        }

        if (query.ToDate.HasValue)
        {
            var toOffset = _clock.EndOfDayExclusive(query.ToDate.Value);
            q = q.Where(b => b.CreatedAt < toOffset);
        }

        q = q.OrderByDescending(b => b.CreatedAt);
        var total = await q.CountAsync(cancellationToken);
        var page = await q
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var items = page.Select(ToBatchSummary).ToList();
        return new PaginatedList<RecruitmentExportBatchSummaryDto>(items, total, query.PageNumber, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<RecruitmentExportBatchDetailDto> GetBatchAsync(
        Guid exportBatchId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Export.ViewHistory,
            clientCenterId,
            cancellationToken);

        var batch = await _exports.GetBatchByIdInCenterAsync(
            exportBatchId, centerId, asNoTracking: true, cancellationToken)
            ?? throw new RecruitmentExportBatchNotFoundException();

        var items = await _exports.QueryItems(asNoTracking: true)
            .Where(i => i.ExportBatchId == exportBatchId)
            .OrderBy(i => i.FileNumberSnapshot)
            .ToListAsync(cancellationToken);

        var memberIds = items.Select(i => i.MemberId).Distinct().ToList();
        var names = await _members.Query(asNoTracking: true)
            .Where(m => memberIds.Contains(m.MemberId))
            .Select(m => new { m.MemberId, m.FullName })
            .ToDictionaryAsync(x => x.MemberId, x => x.FullName, cancellationToken);

        var resultStatuses = await _referenceData.ResultStatuses(asNoTracking: true)
            .ToDictionaryAsync(r => r.StatusId, r => r.NameAr, cancellationToken);

        var summary = ToBatchSummary(batch);
        return new RecruitmentExportBatchDetailDto
        {
            ExportBatchId = summary.ExportBatchId,
            Status = summary.Status,
            RecordCount = summary.RecordCount,
            CenterCodeSnapshot = summary.CenterCodeSnapshot,
            CenterNameSnapshot = summary.CenterNameSnapshot,
            ExportedByUserId = summary.ExportedByUserId,
            GeneratedFileId = summary.GeneratedFileId,
            CreatedAt = summary.CreatedAt,
            CompletedAt = summary.CompletedAt,
            DownloadPath = summary.DownloadPath,
            Items = items.Select(i => new RecruitmentExportItemDto
            {
                ExportBatchId = i.ExportBatchId,
                MemberId = i.MemberId,
                FinalDecisionId = i.FinalDecisionId,
                FileNumberSnapshot = i.FileNumberSnapshot,
                RecruitmentNumberSnapshot = i.RecruitmentNumberSnapshot,
                FullName = names.TryGetValue(i.MemberId, out var n) ? n : null,
                ResultStatusId = i.ResultStatusId,
                ResultStatusNameAr = resultStatuses.TryGetValue(i.ResultStatusId, out var rn) ? rn : null,
                ResultReasonSnapshot = i.ResultReasonSnapshot,
                DecisionAtSnapshot = i.DecisionAtSnapshot,
                DataRevisionSnapshot = i.DataRevisionSnapshot,
                IsReexport = i.IsReexport,
                PreviousExportBatchId = i.PreviousExportBatchId,
                ExportedAt = i.ExportedAt,
                RecruitmentCenterId = i.RecruitmentCenterId,
                RecruitmentCenterCodeSnapshot = i.RecruitmentCenterCodeSnapshot,
                RecruitmentCenterNameSnapshot = i.RecruitmentCenterNameSnapshot
            }).ToList()
        };
    }

    /// <inheritdoc />
    public async Task<ExportFileDto> DownloadBatchFileAsync(
        Guid exportBatchId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Export.Recruitment,
            clientCenterId,
            cancellationToken);
        var userId = RequireUserId();

        var batch = await _exports.GetBatchByIdInCenterAsync(
            exportBatchId, centerId, asNoTracking: true, cancellationToken)
            ?? throw new RecruitmentExportBatchNotFoundException();

        if (batch.Status != StatusCompleted || batch.GeneratedFileId is null)
            throw new RecruitmentExportFileNotReadyException();

        var asset = await _attachments.FindByIdAsync(batch.GeneratedFileId.Value, cancellationToken)
            ?? throw new RecruitmentExportFileNotReadyException();

        if (asset.IsDeleted || asset.CenterId != centerId)
            throw new RecruitmentExportBatchNotFoundException();

        byte[] bytes;
        try
        {
            bytes = _fileStorage.ReadFileBytes(asset.StorageKey);
        }
        catch (InvalidFileException)
        {
            throw new RecruitmentExportFileNotReadyException();
        }

        RecordAudit(
            userId,
            centerId,
            BatchDownloadedEventType,
            "SUCCESS",
            nameof(RecruitmentExportBatch),
            exportBatchId.ToString(),
            "تنزيل ملف دفعة تصدير تجنيد",
            oldValues: null,
            newValues: JsonSerializer.Serialize(new
            {
                exportBatchId,
                asset.FileAssetId,
                asset.OriginalName,
                asset.SizeBytes,
                asset.MimeType
                // لا يُسجَّل StorageKey.
            }));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ExportFileDto
        {
            FileContents = bytes,
            ContentType = string.IsNullOrWhiteSpace(asset.MimeType) ? ExcelMime : asset.MimeType,
            FileName = string.IsNullOrWhiteSpace(asset.OriginalName)
                ? $"recruitment-export-{exportBatchId:N}.xlsx"
                : asset.OriginalName
        };
    }

    /// <inheritdoc />
    public async Task<PaginatedList<RecruitmentExportHistoryItemDto>> ListHistoryAsync(
        RecruitmentExportHistoryQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new RecruitmentExportHistoryQuery();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Export.ViewHistory,
            clientCenterId,
            cancellationToken);

        NormalizeHistoryQuery(query);
        var filtered = await BuildHistoryAsync(centerId, query, cancellationToken);
        var total = filtered.Count;
        var page = filtered
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new PaginatedList<RecruitmentExportHistoryItemDto>(page, total, query.PageNumber, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<ExportFileDto> ExportHistoryExcelAsync(
        RecruitmentExportHistoryQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new RecruitmentExportHistoryQuery();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Export.ViewHistory,
            clientCenterId,
            cancellationToken);
        var userId = RequireUserId();

        NormalizeHistoryQuery(query);
        var rows = await BuildHistoryAsync(centerId, query, cancellationToken);
        if (rows.Count == 0)
            throw new RecruitmentExportEmptyException("لا توجد سجلات مصدَّرة ضمن التصفية الحالية.");
        if (rows.Count > MaxHistoryExportRows)
        {
            throw new InvalidRecruitmentExportDataException(
                $"عدد السجلات ({rows.Count}) يتجاوز حد التصدير ({MaxHistoryExportRows}). ضيّق التصفية.");
        }

        var center = await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken);
        var excelRows = rows.Select(r => new RecruitmentExportHistoryExcelRowDto
        {
            FileNumber = r.FileNumber,
            FullName = r.FullName,
            RecruitmentNumber = r.RecruitmentNumber,
            ResultNameAr = r.ResultStatusNameAr,
            DecisionAt = r.DecisionAt,
            ExportedAt = r.ExportedAt,
            ExportedByUserId = r.ExportedByUserId,
            IsReexport = r.IsReexport
        }).ToList();

        var file = await _export.ExportToExcelAsync(excelRows, new ExportOptions
        {
            ReportName = "سجل-التصدير-للتجنيد",
            SheetName = "سجل المصدَّر",
            CustomTitle = "سجل الملفات المصدَّرة للتجنيد",
            BranchName = center?.NameAr,
            IncludeSummary = false,
            ColumnMappings = new Dictionary<string, string>
            {
                [nameof(RecruitmentExportHistoryExcelRowDto.FileNumber)] = "رقم الملف",
                [nameof(RecruitmentExportHistoryExcelRowDto.FullName)] = "الاسم",
                [nameof(RecruitmentExportHistoryExcelRowDto.RecruitmentNumber)] = "رقم التجنيد",
                [nameof(RecruitmentExportHistoryExcelRowDto.ResultNameAr)] = "النتيجة",
                [nameof(RecruitmentExportHistoryExcelRowDto.DecisionAt)] = "تاريخ القرار",
                [nameof(RecruitmentExportHistoryExcelRowDto.ExportedAt)] = "تاريخ التصدير",
                [nameof(RecruitmentExportHistoryExcelRowDto.ExportedByUserId)] = "المنفّذ",
                [nameof(RecruitmentExportHistoryExcelRowDto.IsReexport)] = "إعادة تصدير"
            }
        });

        RecordAudit(
            userId,
            centerId,
            HistoryExportedEventType,
            "SUCCESS",
            "RecruitmentExportHistory",
            centerId.ToString(),
            "تصدير سجل الملفات المصدَّرة إلى Excel",
            oldValues: null,
            newValues: JsonSerializer.Serialize(new { RecordCount = rows.Count }));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return file;
    }

    #endregion

    #region Private Helpers

    private async Task<(List<Guid> Ids, List<RecruitmentExportExclusionDto> Exclusions)> ResolveMemberIdsAsync(
        Guid centerId,
        CreateRecruitmentExportBatchRequest request,
        CancellationToken cancellationToken)
    {
        var exclusions = new List<RecruitmentExportExclusionDto>();

        if (request.ExportAllPending)
        {
            var pendingQuery = new RecruitmentExportPendingQuery
            {
                ResultStatusId = request.ResultStatusId,
                DecisionFrom = request.DecisionFrom,
                DecisionTo = request.DecisionTo,
                FileNumber = request.FileNumber,
                RecruitmentNumber = request.RecruitmentNumber,
                FullName = request.FullName,
                PageNumber = 1,
                PageSize = MaxBatchMembers
            };
            NormalizePendingQuery(pendingQuery);
            var rows = await BuildPendingRowsAsync(centerId, pendingQuery, cancellationToken);
            return (rows.Select(r => r.MemberId).ToList(), exclusions);
        }

        var raw = request.MemberIds ?? Array.Empty<Guid>();
        if (raw.Count == 0)
            throw new InvalidRecruitmentExportDataException(
                "حدد معرفات المنتسبين أو فعّل تصدير جميع الملفات المعلقة.");

        var distinct = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var id in raw)
        {
            if (id == Guid.Empty)
            {
                exclusions.Add(new RecruitmentExportExclusionDto
                {
                    MemberId = id,
                    ReasonAr = "معرف منتسب غير صالح."
                });
                continue;
            }

            if (!seen.Add(id))
            {
                exclusions.Add(new RecruitmentExportExclusionDto
                {
                    MemberId = id,
                    ReasonAr = "تكرار المنتسب داخل نفس طلب التصدير."
                });
                continue;
            }

            distinct.Add(id);
        }

        return (distinct, exclusions);
    }

    private async Task<List<EligibleRow>> LoadEligibleAsync(
        Guid centerId,
        IReadOnlyList<Guid> memberIds,
        List<RecruitmentExportExclusionDto> exclusions,
        CancellationToken cancellationToken)
    {
        // asNoTracking: أهلية التصدير قراءة فقط؛ تعليم EXPORTED يتم لاحقاً عبر تحديث أعمدة التصدير فقط.
        var members = await _members.Query(asNoTracking: true)
            .Include(m => m.RecruitmentCenter)
            .Where(m => memberIds.Contains(m.MemberId) && m.CurrentMedicalCenterId == centerId)
            .ToListAsync(cancellationToken);

        var decisions = await _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
            .Where(d => d.CenterId == centerId && memberIds.Contains(d.MemberId))
            .ToListAsync(cancellationToken);

        var decisionByMember = decisions.ToDictionary(d => d.MemberId);
        var memberById = members.ToDictionary(m => m.MemberId);
        var result = new List<EligibleRow>();

        foreach (var id in memberIds)
        {
            if (!memberById.TryGetValue(id, out var member))
            {
                exclusions.Add(new RecruitmentExportExclusionDto
                {
                    MemberId = id,
                    ReasonAr = "الملف غير موجود ضمن المركز النشط."
                });
                continue;
            }

            if (!decisionByMember.TryGetValue(member.MemberId, out var decision)
                || !FinalDecisionService.IsEligibleForRecruitmentExport(
                    decision.ResultStatusId,
                    member.ExportStatus)
                || (decision.ResultStatusId == FinalDecisionService.RejectedResultStatusId
                    && string.IsNullOrWhiteSpace(decision.RejectionReason)))
            {
                exclusions.Add(new RecruitmentExportExclusionDto
                {
                    MemberId = id,
                    ReasonAr = "الملف لم يعد مؤهلاً للتصدير (قرار ناقص/مؤجل أو سبق تصديره)."
                });
                continue;
            }

            if (!FinalDecisionService.IsCurrentRevisionReviewed(
                    member.DataRevision,
                    member.ReviewedRevision))
            {
                exclusions.Add(new RecruitmentExportExclusionDto
                {
                    MemberId = id,
                    ReasonAr = "النسخة الحالية من الملف غير مُراجَعة؛ أكّد مراجعة النسخة الحالية أو اعتمد/عدّل القرار النهائي."
                });
                continue;
            }

            result.Add(new EligibleRow(member, decision, member.RecruitmentCenter));
        }

        return result;
    }

    private async Task<List<RecruitmentExportPendingItemDto>> BuildPendingRowsAsync(
        Guid centerId,
        RecruitmentExportPendingQuery query,
        CancellationToken cancellationToken)
    {
        if (query.ResultStatusId is short filterResult
            && filterResult is not FinalDecisionService.AcceptedResultStatusId
            and not FinalDecisionService.RejectedResultStatusId)
        {
            throw new InvalidRecruitmentExportDataException(
                "تصفية النتيجة للتصدير تقتصر على مقبول أو مرفوض.");
        }

        // FR-058: الجاهز للتصدير = قرار نهائي غير مصدَّر + النسخة الحالية مُراجَعة (reviewedRevision = dataRevision).
        var membersQ = _members.Query(asNoTracking: true)
            .Where(m =>
                m.CurrentMedicalCenterId == centerId
                && m.ExportStatus == FinalDecisionService.ExportStatusUnexported
                && m.ReviewedRevision != null
                && m.ReviewedRevision == m.DataRevision);

        if (query.FileNumber.HasValue)
            membersQ = membersQ.Where(m => m.FileNumber == query.FileNumber.Value);

        if (!string.IsNullOrWhiteSpace(query.RecruitmentNumber))
        {
            var rn = query.RecruitmentNumber.Trim().ToLowerInvariant();
            membersQ = membersQ.Where(m => m.RecruitmentNumber.ToLower() == rn);
        }

        if (!string.IsNullOrWhiteSpace(query.FullName))
        {
            var name = query.FullName.Trim();
            membersQ = membersQ.Where(m => m.FullName.Contains(name));
        }

        var decisionsQ = _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
            .Where(d =>
                d.CenterId == centerId
                && (d.ResultStatusId == FinalDecisionService.AcceptedResultStatusId
                    || d.ResultStatusId == FinalDecisionService.RejectedResultStatusId));

        if (query.ResultStatusId.HasValue)
            decisionsQ = decisionsQ.Where(d => d.ResultStatusId == query.ResultStatusId.Value);

        if (query.DecisionFrom.HasValue)
        {
            var from = _clock.StartOfDay(query.DecisionFrom.Value);
            decisionsQ = decisionsQ.Where(d => d.DecidedAt >= from);
        }

        if (query.DecisionTo.HasValue)
        {
            var toExclusive = _clock.EndOfDayExclusive(query.DecisionTo.Value);
            decisionsQ = decisionsQ.Where(d => d.DecidedAt < toExclusive);
        }

        var joined = await (
            from m in membersQ
            join d in decisionsQ on m.MemberId equals d.MemberId
            join rc in _referenceData.RecruitmentCenters(asNoTracking: true)
                on m.RecruitmentCenterId equals rc.RecruitmentCenterId
            join rs in _referenceData.ResultStatuses(asNoTracking: true)
                on d.ResultStatusId equals rs.StatusId
            orderby d.DecidedAt descending, m.FileNumber
            select new
            {
                m.MemberId,
                m.FileNumber,
                m.RecruitmentNumber,
                m.FullName,
                m.RecruitmentCenterId,
                RecruitmentCenterCode = rc.Code,
                RecruitmentCenterNameAr = rc.NameAr,
                d.FinalDecisionId,
                d.ResultStatusId,
                ResultStatusNameAr = rs.NameAr,
                ResultReason = d.RejectionReason,
                d.DecidedAt,
                m.DataRevision
            }).ToListAsync(cancellationToken);

        var memberIds = joined.Select(x => x.MemberId).ToList();
        var priorExports = await (
            from item in _exports.QueryItems(asNoTracking: true)
            join batch in _exports.QueryBatches(asNoTracking: true)
                on item.ExportBatchId equals batch.ExportBatchId
            where memberIds.Contains(item.MemberId) && batch.Status == StatusCompleted
            select item.MemberId
        ).Distinct().ToListAsync(cancellationToken);
        var priorSet = priorExports.ToHashSet();

        return joined.Select(x => new RecruitmentExportPendingItemDto
        {
            MemberId = x.MemberId,
            FileNumber = x.FileNumber,
            RecruitmentNumber = x.RecruitmentNumber,
            FullName = x.FullName,
            RecruitmentCenterId = x.RecruitmentCenterId,
            RecruitmentCenterCode = x.RecruitmentCenterCode,
            RecruitmentCenterNameAr = x.RecruitmentCenterNameAr,
            FinalDecisionId = x.FinalDecisionId,
            ResultStatusId = x.ResultStatusId,
            ResultStatusNameAr = x.ResultStatusNameAr,
            ResultReason = x.ResultReason,
            DecidedAt = x.DecidedAt,
            DataRevision = x.DataRevision,
            IsReexportCandidate = priorSet.Contains(x.MemberId)
        }).ToList();
    }

    private async Task<List<RecruitmentExportHistoryItemDto>> BuildHistoryAsync(
        Guid centerId,
        RecruitmentExportHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var q =
            from item in _exports.QueryItems(asNoTracking: true)
            join batch in _exports.QueryBatches(asNoTracking: true)
                on item.ExportBatchId equals batch.ExportBatchId
            join m in _members.Query(asNoTracking: true) on item.MemberId equals m.MemberId
            join rs in _referenceData.ResultStatuses(asNoTracking: true)
                on item.ResultStatusId equals rs.StatusId
            where batch.CenterId == centerId && batch.Status == StatusCompleted
            select new { item, batch, m, rs };

        if (query.FileNumber.HasValue)
            q = q.Where(x => x.item.FileNumberSnapshot == query.FileNumber.Value);

        if (!string.IsNullOrWhiteSpace(query.RecruitmentNumber))
        {
            var rn = query.RecruitmentNumber.Trim().ToLowerInvariant();
            q = q.Where(x => x.item.RecruitmentNumberSnapshot.ToLower() == rn);
        }

        if (!string.IsNullOrWhiteSpace(query.FullName))
        {
            var name = query.FullName.Trim();
            q = q.Where(x => x.m.FullName.Contains(name));
        }

        if (query.ResultStatusId.HasValue)
            q = q.Where(x => x.item.ResultStatusId == query.ResultStatusId.Value);

        if (query.ExportedFrom.HasValue)
        {
            var from = _clock.StartOfDay(query.ExportedFrom.Value);
            q = q.Where(x => x.item.ExportedAt >= from);
        }

        if (query.ExportedTo.HasValue)
        {
            var toExclusive = _clock.EndOfDayExclusive(query.ExportedTo.Value);
            q = q.Where(x => x.item.ExportedAt < toExclusive);
        }

        var rows = await q
            .OrderByDescending(x => x.item.ExportedAt)
            .Select(x => new RecruitmentExportHistoryItemDto
            {
                ExportBatchId = x.item.ExportBatchId,
                MemberId = x.item.MemberId,
                FileNumber = x.item.FileNumberSnapshot,
                FullName = x.m.FullName,
                RecruitmentNumber = x.item.RecruitmentNumberSnapshot,
                ResultStatusId = x.item.ResultStatusId,
                ResultStatusNameAr = x.rs.NameAr,
                ResultReason = x.item.ResultReasonSnapshot,
                DecisionAt = x.item.DecisionAtSnapshot,
                ExportedAt = x.item.ExportedAt,
                ExportedByUserId = x.batch.ExportedByUserId,
                IsReexport = x.item.IsReexport,
                PreviousExportBatchId = x.item.PreviousExportBatchId,
                BatchStatus = x.batch.Status
            })
            .ToListAsync(cancellationToken);

        return rows;
    }

    private static RecruitmentExportBatchSummaryDto ToBatchSummary(RecruitmentExportBatch batch)
    {
        return new RecruitmentExportBatchSummaryDto
        {
            ExportBatchId = batch.ExportBatchId,
            Status = batch.Status,
            RecordCount = batch.RecordCount,
            CenterCodeSnapshot = batch.CenterCodeSnapshot,
            CenterNameSnapshot = batch.CenterNameSnapshot,
            ExportedByUserId = batch.ExportedByUserId,
            GeneratedFileId = batch.GeneratedFileId,
            CreatedAt = batch.CreatedAt,
            CompletedAt = batch.CompletedAt,
            DownloadPath = batch.Status == StatusCompleted && batch.GeneratedFileId.HasValue
                ? string.Format(DownloadPathFormat, batch.ExportBatchId)
                : null
        };
    }

    private void NormalizePendingQuery(RecruitmentExportPendingQuery query)
    {
        if (query.PageNumber < 1) query.PageNumber = 1;
        if (query.PageSize < 1) query.PageSize = 20;
        if (query.PageSize > MaxPageSize) query.PageSize = MaxPageSize;
        if (query.DecisionFrom.HasValue && query.DecisionTo.HasValue && query.DecisionFrom > query.DecisionTo)
            throw new InvalidRecruitmentExportDataException("تاريخ بداية القرار بعد تاريخ نهايته.");
    }

    private void NormalizeHistoryQuery(RecruitmentExportHistoryQuery query)
    {
        if (query.PageNumber < 1) query.PageNumber = 1;
        if (query.PageSize < 1) query.PageSize = 20;
        if (query.PageSize > MaxPageSize) query.PageSize = MaxPageSize;
        if (query.ExportedFrom.HasValue && query.ExportedTo.HasValue && query.ExportedFrom > query.ExportedTo)
            throw new InvalidRecruitmentExportDataException("تاريخ بداية التصدير بعد تاريخ نهايته.");
    }

    private static void NormalizePaging(RecruitmentExportBatchQuery query)
    {
        if (query.PageNumber < 1) query.PageNumber = 1;
        if (query.PageSize < 1) query.PageSize = 20;
        if (query.PageSize > MaxPageSize) query.PageSize = MaxPageSize;
        if (query.FromDate.HasValue && query.ToDate.HasValue && query.FromDate > query.ToDate)
            throw new InvalidRecruitmentExportDataException("تاريخ البداية بعد تاريخ النهاية.");
    }

    private string RequireUserId()
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
            throw new UnauthorizedAccessException("المستخدم غير مصادق.");
        return _currentUser.UserId;
    }

    private void RecordAudit(
        string userId,
        Guid centerId,
        string eventType,
        string result,
        string entityType,
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
            EntityType = entityType,
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

    private static IFormFile CreateFormFile(byte[] content, string fileName, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? ExcelMime : contentType
        };
    }

    private static string ComputeSha256(byte[] content)
    {
        var hash = SHA256.HashData(content);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "recruitment-export.xlsx";
        var fileName = Path.GetFileName(name);
        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');
        return string.IsNullOrWhiteSpace(fileName) ? "recruitment-export.xlsx" : fileName;
    }

    private sealed record EligibleRow(
        Member Member,
        Domain.Entities.Clinical.FinalDecision Decision,
        RecruitmentCenter RecruitmentCenter);

    #endregion
}
