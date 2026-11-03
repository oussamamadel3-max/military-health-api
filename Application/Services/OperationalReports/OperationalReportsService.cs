using System.Net;
using System.Text.Json;
using Application.Common.Permissions;
using Application.Common.Results;
using Application.Common.Security;
using Application.Dtos.ExportDtos;
using Application.Dtos.OperationalReports;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.Members;
using Application.Services.Supervisor;
using Domain.Entities.Audit;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.OperationalReports;

/// <summary>
/// تقارير ومؤشرات تشغيلية طبية (FR-062…068).
/// لا يستخدم DashboardService/ReportsService القالبيين.
/// منع الازدواج (BR-059 مؤقتاً قبل T-11): احتساب عبر CurrentMedicalCenterId مرة واحدة لكل منتسب.
/// «متأخر» قيمة محسوبة من DeferUntil وليست حالة مخزّنة.
/// الفترة الزمنية: القائمة النهائية على DecidedAt؛ مؤشرات القرار على DecidedAt والانتظار على CreatedAt؛ العيادات على AssessedAt؛ ربط الأطباء على ExaminedAt.
/// </summary>
public class OperationalReportsService : IOperationalReportsService
{
    public const string DeferredStatusRequested = "REQUESTED";
    public const string DeferredStatusCompleted = "COMPLETED";
    public const string DeferredStatusClosed = "CLOSED";
    public const string ExportEventType = "OperationalReportExport";
    public const string CentralViewEventType = "OperationalReportCentralView";
    public const string AntiDoubleCountMethodology =
        "CurrentMedicalCenterId — each member counted once at current center (temporary until MemberTransfer workflow completes BR-059 formally)";

    private const int MaxPageSize = 200;

    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;
    private readonly IMemberRepository _members;
    private readonly IClinicWorkspaceRepository _clinicWorkspace;
    private readonly ICenterClinicRepository _centerClinics;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IMedicalCenterRepository _centers;
    private readonly IMemberArchiveRepository _archives;
    private readonly IUserRepository _users;
    private readonly IExportService _export;
    private readonly ICrossCenterAccessService _crossCenterAccess;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;

    public OperationalReportsService(
        IPermissionGate permissionGate,
        ICurrentUserService currentUser,
        IMemberRepository members,
        IClinicWorkspaceRepository clinicWorkspace,
        ICenterClinicRepository centerClinics,
        IReferenceDataRepository referenceData,
        IMedicalCenterRepository centers,
        IMemberArchiveRepository archives,
        IUserRepository users,
        IExportService export,
        ICrossCenterAccessService crossCenterAccess,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork)
    {
        _permissionGate = permissionGate;
        _currentUser = currentUser;
        _members = members;
        _clinicWorkspace = clinicWorkspace;
        _centerClinics = centerClinics;
        _referenceData = referenceData;
        _centers = centers;
        _archives = archives;
        _users = users;
        _export = export;
        _crossCenterAccess = crossCenterAccess;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    #region Public — KPIs

    /// <inheritdoc />
    public async Task<CenterKpiDto> GetLocalKpisAsync(
        LocalKpiQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new LocalKpiQuery();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Reports.Local, clientCenterId, cancellationToken);
        var range = NormalizeRange(query);
        NormalizeExportStatus(query);
        var center = await RequireCenterAsync(centerId, cancellationToken);
        return await BuildCenterKpiAsync(
            center,
            range,
            resultFilter: query.ResultStatusId,
            exportStatus: query.ExportStatus,
            clinicId: query.ClinicId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CentralKpiResponseDto> GetCentralKpisAsync(
        CentralKpiQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new CentralKpiQuery();
        await _permissionGate.EnsureCentralAsync(
            MilitaryHealthPermissions.Reports.Central, cancellationToken);
        await EnsureNoOrphanMembersAsync(cancellationToken);

        var range = NormalizeRange(query);
        var centers = await ResolveCentersAsync(query.CenterIds, cancellationToken);
        var perCenter = new List<CenterKpiDto>();
        foreach (var center in centers)
        {
            perCenter.Add(await BuildCenterKpiAsync(
                center, range, query.ResultStatusId, exportStatus: null, clinicId: null, cancellationToken));
            await RecordCentralAccessAsync(
                center.CenterId,
                "عرض مؤشرات مركزية مجمعة",
                "OperationalKpi",
                JsonSerializer.Serialize(new { range.FromDate, range.ToDate, query.ResultStatusId }),
                cancellationToken);
        }

        var totals = SumKpis(perCenter);
        RecordAudit(CentralViewEventType, "OperationalKpi", null,
            "اطلاع مركزي على مؤشرات مجمعة",
            new { CenterCount = perCenter.Count, range.FromDate, range.ToDate });
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CentralKpiResponseDto
        {
            AntiDoubleCountMethodology = AntiDoubleCountMethodology,
            Centers = perCenter,
            Totals = totals
        };
    }

    /// <inheritdoc />
    public async Task<ExportFileDto> ExportCentralKpisAsync(
        CentralKpiQuery query,
        CancellationToken cancellationToken = default)
    {
        var data = await GetCentralKpisAsync(query, cancellationToken);
        var totalRow = ToKpiExcel(data.Totals);
        totalRow.CenterCode = "TOTAL";
        totalRow.CenterNameAr = "الإجمالي";
        var rows = data.Centers.Select(ToKpiExcel).Append(totalRow).ToList();
        return await ExportRowsAsync(rows, "مؤشرات-مركزية", "المؤشرات", "تصدير المؤشرات المركزية", cancellationToken);
    }

    #endregion

    #region Public — Final members / local summary

    /// <inheritdoc />
    public async Task<PaginatedList<FinalMemberListItemDto>> ListFinalMembersAsync(
        FinalMembersQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new FinalMembersQuery();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Reports.Local, clientCenterId, cancellationToken);
        NormalizePaging(query);
        var range = NormalizeRange(query);
        NormalizeExportStatus(query);

        var items = await BuildFinalMemberItemsAsync(centerId, query, range, cancellationToken);
        var total = items.Count;
        var page = items
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();
        return new PaginatedList<FinalMemberListItemDto>(page, total, query.PageNumber, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<ExportFileDto> ExportFinalMembersAsync(
        FinalMembersQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new FinalMembersQuery();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Reports.Local, clientCenterId, cancellationToken);
        var range = NormalizeRange(query);
        NormalizeExportStatus(query);
        var items = await BuildFinalMemberItemsAsync(centerId, query, range, cancellationToken);
        if (items.Count == 0)
            throw new OperationalReportExportEmptyException();
        if (items.Count > MemberService.MaxExportRows)
            throw new InvalidOperationalReportFilterException(
                $"عدد السجلات ({items.Count}) يتجاوز حد التصدير ({MemberService.MaxExportRows}).");

        var rows = items.Select(i => new FinalMemberExcelRowDto
        {
            FileNumber = i.FileNumber,
            FullName = i.FullName,
            RecruitmentNumber = i.RecruitmentNumber,
            FinalResultNameAr = i.FinalResultNameAr,
            FinalResultReason = i.FinalResultReason,
            DecidedAt = i.DecidedAt,
            ExportStatus = i.ExportStatus,
            IsArchived = i.IsArchived
        }).ToList();

        return await ExportRowsAsync(
            rows, "القائمة-النهائية", "القائمة النهائية", "تصدير القائمة النهائية للمنتسبين",
            cancellationToken,
            centerId);
    }

    /// <inheritdoc />
    public async Task<PaginatedList<FinalMemberListItemDto>> ListWaitingMembersAsync(
        WaitingMembersQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new WaitingMembersQuery();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Reports.Local, clientCenterId, cancellationToken);
        NormalizePaging(query);
        var range = NormalizeRange(query);
        NormalizeExportStatus(query);

        var filtered = BuildWaitingMembersBaseQuery(centerId, query, range);
        var total = await filtered.CountAsync(cancellationToken);
        var pageMembers = await filtered
            .OrderByDescending(m => m.UpdatedAt)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var items = await EnrichWaitingMemberItemsAsync(centerId, pageMembers, cancellationToken);
        return new PaginatedList<FinalMemberListItemDto>(items, total, query.PageNumber, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<ExportFileDto> ExportWaitingMembersAsync(
        WaitingMembersQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new WaitingMembersQuery();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Reports.Local, clientCenterId, cancellationToken);
        var range = NormalizeRange(query);
        NormalizeExportStatus(query);

        var filtered = BuildWaitingMembersBaseQuery(centerId, query, range);
        var total = await filtered.CountAsync(cancellationToken);
        if (total == 0)
            throw new OperationalReportExportEmptyException();
        if (total > MemberService.MaxExportRows)
            throw new InvalidOperationalReportFilterException(
                $"عدد السجلات ({total}) يتجاوز حد التصدير ({MemberService.MaxExportRows}).");

        var members = await filtered
            .OrderByDescending(m => m.UpdatedAt)
            .ToListAsync(cancellationToken);
        var items = await EnrichWaitingMemberItemsAsync(centerId, members, cancellationToken);

        var rows = items.Select(i => new FinalMemberExcelRowDto
        {
            FileNumber = i.FileNumber,
            FullName = i.FullName,
            RecruitmentNumber = i.RecruitmentNumber,
            FinalResultNameAr = i.FinalResultNameAr,
            FinalResultReason = i.FinalResultReason,
            DecidedAt = i.DecidedAt,
            ExportStatus = i.ExportStatus,
            IsArchived = i.IsArchived
        }).ToList();

        return await ExportRowsAsync(
            rows, "قيد-الانتظار", "قيد الانتظار", "تصدير قائمة قيد الانتظار",
            cancellationToken,
            centerId);
    }

    /// <inheritdoc />
    public async Task<LocalSummaryDto> GetLocalSummaryAsync(
        LocalSummaryQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new LocalSummaryQuery();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Reports.Local, clientCenterId, cancellationToken);
        var range = NormalizeRange(query);
        var center = await RequireCenterAsync(centerId, cancellationToken);
        var kpi = await BuildCenterKpiAsync(
            center, range, query.ResultStatusId, exportStatus: null, query.ClinicId, cancellationToken);
        var byClinic = await BuildClinicBreakdownAsync(centerId, range, query.ClinicId, query.ResultStatusId, cancellationToken);

        return new LocalSummaryDto
        {
            CenterId = center.CenterId,
            CenterCode = center.Code,
            CenterNameAr = center.NameAr,
            TotalMembers = kpi.TotalMembers,
            AcceptedCount = kpi.AcceptedCount,
            RejectedCount = kpi.RejectedCount,
            DeferredCount = kpi.DeferredCount,
            WaitingCount = kpi.WaitingCount,
            AcceptedPercent = Percent(kpi.AcceptedCount, kpi.TotalMembers),
            RejectedPercent = Percent(kpi.RejectedCount, kpi.TotalMembers),
            DeferredPercent = Percent(kpi.DeferredCount, kpi.TotalMembers),
            WaitingPercent = Percent(kpi.WaitingCount, kpi.TotalMembers),
            ByClinic = byClinic
        };
    }

    /// <inheritdoc />
    public async Task<ExportFileDto> ExportLocalSummaryAsync(
        LocalSummaryQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var summary = await GetLocalSummaryAsync(query, clientCenterId, cancellationToken);
        var rows = summary.ByClinic.Select(c => new ClinicResultCountDto
        {
            ClinicId = c.ClinicId,
            ClinicCode = c.ClinicCode,
            ClinicNameAr = c.ClinicNameAr,
            AssessmentCount = c.AssessmentCount,
            AcceptedCount = c.AcceptedCount,
            RejectedCount = c.RejectedCount,
            DeferredCount = c.DeferredCount
        }).ToList();
        if (rows.Count == 0)
            throw new OperationalReportExportEmptyException();
        return await ExportRowsAsync(
            rows, "تقرير-محلي", "حسب العيادة", "تصدير تقرير محلي", cancellationToken, summary.CenterId);
    }

    #endregion

    #region Public — Deferred

    /// <inheritdoc />
    public async Task<DeferredReportResponseDto> GetDeferredReportAsync(
        DeferredReportQuery query,
        Guid? clientCenterId = null,
        bool central = false,
        CancellationToken cancellationToken = default)
    {
        query ??= new DeferredReportQuery();
        Guid? centerId = null;
        if (central)
        {
            await _permissionGate.EnsureCentralAsync(
                MilitaryHealthPermissions.Reports.Central, cancellationToken);
            await EnsureNoOrphanMembersAsync(cancellationToken);
        }
        else
        {
            centerId = await _permissionGate.EnsureLocalAsync(
                MilitaryHealthPermissions.Reports.Local, clientCenterId, cancellationToken);
        }

        NormalizeDeferredQuery(query);
        var range = NormalizeRange(query);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var all = await LoadDeferredItemsAsync(centerId, query, range, today, cancellationToken);

        if (central)
        {
            foreach (var cid in all.Select(x => x.CenterId).Distinct())
            {
                await RecordCentralAccessAsync(
                    cid, "عرض تقرير مؤجلات مركزي", "DeferredReport",
                    JsonSerializer.Serialize(new { query.ActionTypeId, query.ActionStatus }),
                    cancellationToken);
            }
        }

        var breakdown = all
            .GroupBy(x => new { x.ActionTypeId, x.ActionTypeCode, x.ActionTypeNameAr, x.Status })
            .Select(g => new DeferredActionBreakdownDto
            {
                ActionTypeId = g.Key.ActionTypeId,
                ActionTypeCode = g.Key.ActionTypeCode,
                ActionTypeNameAr = g.Key.ActionTypeNameAr,
                Status = g.Key.Status,
                Count = g.Count(),
                LateCount = g.Count(x => x.IsLate)
            })
            .OrderBy(x => x.ActionTypeId).ThenBy(x => x.Status)
            .ToList();

        var total = all.Count;
        var page = all
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new DeferredReportResponseDto
        {
            TotalCount = total,
            LateCount = all.Count(x => x.IsLate),
            Breakdown = breakdown,
            Items = page,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            TotalPages = (int)Math.Ceiling(total / (double)query.PageSize)
        };
    }

    /// <inheritdoc />
    public async Task<ExportFileDto> ExportDeferredReportAsync(
        DeferredReportQuery query,
        Guid? clientCenterId = null,
        bool central = false,
        CancellationToken cancellationToken = default)
    {
        query ??= new DeferredReportQuery();
        query.PageNumber = 1;
        query.PageSize = MemberService.MaxExportRows;
        var report = await GetDeferredReportAsync(query, clientCenterId, central, cancellationToken);
        if (report.TotalCount == 0)
            throw new OperationalReportExportEmptyException();

        var rows = report.Items.Select(i => new DeferredExcelRowDto
        {
            FileNumber = i.FileNumber,
            FullName = i.FullName,
            ActionTypeNameAr = i.ActionTypeNameAr,
            Status = i.Status,
            DeferUntil = i.DeferUntil,
            IsLate = i.IsLate,
            RequestedAt = i.RequestedAt
        }).ToList();

        return await ExportRowsAsync(
            rows, "تقرير-المؤجلات", "المؤجلات", "تصدير تقرير الحالات المؤجلة",
            cancellationToken);
    }

    #endregion

    #region Public — Central compare / doctor linkage

    /// <inheritdoc />
    public async Task<CentralCompareResponseDto> GetCentralCompareAsync(
        CentralCompareQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new CentralCompareQuery();
        await _permissionGate.EnsureCentralAsync(
            MilitaryHealthPermissions.Reports.Central, cancellationToken);
        await EnsureNoOrphanMembersAsync(cancellationToken);
        var range = NormalizeRange(query);
        NormalizeExportStatus(query);

        var centers = await ResolveCentersAsync(query.CenterIds, cancellationToken);
        var rows = new List<CentralCompareCenterDto>();
        foreach (var center in centers)
        {
            rows.Add(await BuildCompareRowAsync(center, range, query, cancellationToken));
            await RecordCentralAccessAsync(
                center.CenterId, "مقارنة مراكز — تقرير مركزي", "CentralCompare",
                JsonSerializer.Serialize(new { range.FromDate, range.ToDate, query.ResultStatusId, query.ExportStatus, query.IsArchived }),
                cancellationToken);
        }

        RecordAudit(CentralViewEventType, "CentralCompare", null, "اطلاع مركزي مقارن",
            new { CenterCount = rows.Count });
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CentralCompareResponseDto
        {
            AntiDoubleCountMethodology = AntiDoubleCountMethodology,
            Centers = rows,
            Totals = new CentralCompareCenterDto
            {
                CenterId = Guid.Empty,
                CenterCode = "TOTAL",
                CenterNameAr = "الإجمالي",
                TotalMembers = rows.Sum(r => r.TotalMembers),
                AcceptedCount = rows.Sum(r => r.AcceptedCount),
                RejectedCount = rows.Sum(r => r.RejectedCount),
                DeferredCount = rows.Sum(r => r.DeferredCount),
                WaitingCount = rows.Sum(r => r.WaitingCount),
                ExportedCount = rows.Sum(r => r.ExportedCount),
                UnexportedCount = rows.Sum(r => r.UnexportedCount),
                ArchivedCount = rows.Sum(r => r.ArchivedCount)
            }
        };
    }

    /// <inheritdoc />
    public async Task<ExportFileDto> ExportCentralCompareAsync(
        CentralCompareQuery query,
        CancellationToken cancellationToken = default)
    {
        var data = await GetCentralCompareAsync(query, cancellationToken);
        var rows = data.Centers.Select(c => new CenterKpiExcelRowDto
        {
            CenterCode = c.CenterCode,
            CenterNameAr = c.CenterNameAr,
            TotalMembers = c.TotalMembers,
            AcceptedCount = c.AcceptedCount,
            RejectedCount = c.RejectedCount,
            DeferredCount = c.DeferredCount,
            WaitingCount = c.WaitingCount,
            ExportedCount = c.ExportedCount,
            UnexportedCount = c.UnexportedCount
        }).ToList();
        if (rows.Count == 0)
            throw new OperationalReportExportEmptyException();
        return await ExportRowsAsync(rows, "مقارنة-مراكز", "المقارنة", "تصدير المقارنة المركزية", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PaginatedList<DoctorLinkageByMemberItemDto>> ListDoctorLinkageByMemberAsync(
        DoctorLinkageQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new DoctorLinkageQuery();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Reports.Local, clientCenterId, cancellationToken);
        NormalizeDoctorQuery(query);
        var range = NormalizeRange(query);

        var items = await BuildDoctorByMemberAsync(centerId, query, range, cancellationToken);
        var total = items.Count;
        var page = items.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToList();
        return new PaginatedList<DoctorLinkageByMemberItemDto>(page, total, query.PageNumber, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<PaginatedList<DoctorLinkageByDoctorItemDto>> ListDoctorLinkageByDoctorAsync(
        DoctorLinkageQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new DoctorLinkageQuery();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Reports.Local, clientCenterId, cancellationToken);
        NormalizeDoctorQuery(query);
        var range = NormalizeRange(query);

        var items = await BuildDoctorByDoctorAsync(centerId, query, range, cancellationToken);
        var total = items.Count;
        var page = items.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToList();
        return new PaginatedList<DoctorLinkageByDoctorItemDto>(page, total, query.PageNumber, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<ExportFileDto> ExportDoctorLinkageAsync(
        DoctorLinkageQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new DoctorLinkageQuery();
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Reports.Local, clientCenterId, cancellationToken);
        NormalizeDoctorQuery(query);
        var range = NormalizeRange(query);

        List<DoctorLinkageExcelRowDto> rows;
        if (string.Equals(query.ViewMode, DoctorLinkageViewModes.ByDoctor, StringComparison.OrdinalIgnoreCase))
        {
            var items = await BuildDoctorByDoctorAsync(centerId, query, range, cancellationToken);
            rows = items.Select(i => new DoctorLinkageExcelRowDto
            {
                ExaminerUserId = i.ExaminerUserId,
                ExaminerDisplayName = i.ExaminerDisplayName,
                ClinicCode = i.ClinicCode,
                MemberCount = i.MemberCount,
                ResultNameAr = $"مقبول:{i.AcceptedCount}/مرفوض:{i.RejectedCount}/مؤجل:{i.DeferredCount}"
            }).ToList();
        }
        else
        {
            var items = await BuildDoctorByMemberAsync(centerId, query, range, cancellationToken);
            rows = items.SelectMany(m => m.Exams.Select(e => new DoctorLinkageExcelRowDto
            {
                FileNumber = m.FileNumber,
                MemberName = m.FullName,
                ExaminerUserId = e.ExaminerUserId,
                ExaminerDisplayName = e.ExaminerDisplayName,
                ClinicCode = e.ClinicCode,
                ExaminedAt = e.ExaminedAt,
                ResultNameAr = e.ResultNameAr
            })).ToList();
        }

        if (rows.Count == 0)
            throw new OperationalReportExportEmptyException();
        return await ExportRowsAsync(
            rows, "ربط-الأطباء", "الربط", "تصدير تقرير ربط المنتسبين بالأطباء",
            cancellationToken, centerId);
    }

    #endregion

    #region KPI builders

    private async Task<CenterKpiDto> BuildCenterKpiAsync(
        MedicalCenter center,
        DateRange range,
        short? resultFilter,
        string? exportStatus,
        Guid? clinicId,
        CancellationToken cancellationToken)
    {
        // فترة محددة: نتائج القرار على DecidedAt؛ الانتظار على CreatedAt لمن بلا قرار.
        // بلا فترة: كل منتسبي المركز الحالي (سلوك سابق عبر CreatedAt غير مقيّد).
        var hasPeriod = range.From.HasValue || range.ToExclusive.HasValue;

        List<(Guid MemberId, string ExportStatus)> members;
        List<(Guid MemberId, short ResultStatusId)> decisionRows;

        if (hasPeriod)
        {
            var decided = await FinalDecisionsInCenter(center.CenterId, range)
                .Select(d => new { d.MemberId, d.ResultStatusId })
                .ToListAsync(cancellationToken);

            var currentInCenter = await MembersCurrentlyInCenter(center.CenterId)
                .Select(m => new { m.MemberId, m.ExportStatus })
                .ToListAsync(cancellationToken);
            var currentMap = currentInCenter.ToDictionary(m => m.MemberId, m => m.ExportStatus);
            decided = decided.Where(d => currentMap.ContainsKey(d.MemberId)).ToList();

            if (resultFilter.HasValue)
                decided = decided.Where(d => d.ResultStatusId == resultFilter.Value).ToList();

            var decidedMembers = decided
                .Select(d => (d.MemberId, ExportStatus: currentMap[d.MemberId]))
                .GroupBy(x => x.MemberId)
                .Select(g => g.First())
                .ToList();

            var registeredWaiting = await ApplyCreatedAtRange(MembersCurrentlyInCenter(center.CenterId), range)
                .Select(m => new { m.MemberId, m.ExportStatus })
                .ToListAsync(cancellationToken);
            var waitingCandidateIds = registeredWaiting.Select(r => r.MemberId).ToList();
            var anyDecisionIds = waitingCandidateIds.Count == 0
                ? new List<Guid>()
                : await _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
                    .Where(d => d.CenterId == center.CenterId && waitingCandidateIds.Contains(d.MemberId))
                    .Select(d => d.MemberId)
                    .Distinct()
                    .ToListAsync(cancellationToken);
            var anyDecisionSet = anyDecisionIds.ToHashSet();
            var waitingMembers = registeredWaiting
                .Where(m => !anyDecisionSet.Contains(m.MemberId))
                .Select(m => (m.MemberId, m.ExportStatus))
                .ToList();

            if (resultFilter.HasValue)
                waitingMembers = new List<(Guid MemberId, string ExportStatus)>();

            members = decidedMembers
                .Concat(waitingMembers)
                .GroupBy(m => m.MemberId)
                .Select(g => g.First())
                .ToList();
            decisionRows = decided
                .GroupBy(d => d.MemberId)
                .Select(g => (g.Key, g.First().ResultStatusId))
                .ToList();
        }
        else
        {
            var membersQ = MembersCurrentlyInCenter(center.CenterId);
            if (!string.IsNullOrWhiteSpace(exportStatus))
                membersQ = membersQ.Where(m => m.ExportStatus == exportStatus);

            members = (await membersQ.Select(m => new { m.MemberId, m.ExportStatus })
                    .ToListAsync(cancellationToken))
                .Select(m => (m.MemberId, m.ExportStatus))
                .ToList();

            var memberIdsNoPeriod = members.Select(m => m.MemberId).ToList();
            decisionRows = (await _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
                    .Where(d => d.CenterId == center.CenterId && memberIdsNoPeriod.Contains(d.MemberId))
                    .Select(d => new { d.MemberId, d.ResultStatusId })
                    .ToListAsync(cancellationToken))
                .Select(d => (d.MemberId, d.ResultStatusId))
                .ToList();

            if (resultFilter.HasValue)
            {
                var matching = decisionRows
                    .Where(d => d.ResultStatusId == resultFilter.Value)
                    .Select(d => d.MemberId)
                    .ToHashSet();
                members = members.Where(m => matching.Contains(m.MemberId)).ToList();
                decisionRows = decisionRows.Where(d => matching.Contains(d.MemberId)).ToList();
            }
        }

            if (!string.IsNullOrWhiteSpace(exportStatus) && hasPeriod)
        {
            members = members
                .Where(m => string.Equals(m.ExportStatus, exportStatus, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var keep = members.Select(m => m.MemberId).ToHashSet();
            decisionRows = decisionRows.Where(d => keep.Contains(d.MemberId)).ToList();
        }

        var memberIds = members.Select(m => m.MemberId).ToList();

        if (clinicId.HasValue && memberIds.Count > 0)
        {
            var inClinic = await _clinicWorkspace.QueryClinicAssessments(asNoTracking: true)
                .Where(a => a.CenterId == center.CenterId
                            && a.ClinicId == clinicId.Value
                            && memberIds.Contains(a.MemberId))
                .Select(a => a.MemberId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var clinicSet = inClinic.ToHashSet();
            members = members.Where(m => clinicSet.Contains(m.MemberId)).ToList();
            memberIds = members.Select(m => m.MemberId).ToList();
            decisionRows = decisionRows.Where(d => clinicSet.Contains(d.MemberId)).ToList();
        }

        var decisionByMember = decisionRows
            .GroupBy(d => d.MemberId)
            .ToDictionary(g => g.Key, g => g.First().ResultStatusId);

        var accepted = decisionByMember.Count(kv => kv.Value == FinalDecisionService.AcceptedResultStatusId);
        var rejected = decisionByMember.Count(kv => kv.Value == FinalDecisionService.RejectedResultStatusId);
        var deferred = decisionByMember.Count(kv => kv.Value == FinalDecisionService.DeferredResultStatusId);
        var waiting = members.Count(m => !decisionByMember.ContainsKey(m.MemberId));

        // عند تصفية النتيجة لا نخلط الانتظار مع نتائج أخرى.
        if (resultFilter.HasValue)
            waiting = 0;

        var mandatory = await LoadMandatoryAsync(center.CenterId, cancellationToken);
        var assessments = await _clinicWorkspace.QueryClinicAssessments(asNoTracking: true)
            .Where(a => a.CenterId == center.CenterId && memberIds.Contains(a.MemberId))
            .Select(a => new { a.MemberId, a.ClinicId })
            .ToListAsync(cancellationToken);
        var assessedByMember = assessments
            .GroupBy(a => a.MemberId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ClinicId).ToHashSet());

        var complete = 0;
        var incomplete = 0;
        foreach (var m in members)
        {
            assessedByMember.TryGetValue(m.MemberId, out var set);
            set ??= new HashSet<Guid>();
            if (MandatoryClinicCompleteness.AreComplete(mandatory, set))
                complete++;
            else
                incomplete++;
        }

        var deferredBreakdown = await BuildDeferredBreakdownAsync(center.CenterId, range, cancellationToken);

        return new CenterKpiDto
        {
            CenterId = center.CenterId,
            CenterCode = center.Code,
            CenterNameAr = center.NameAr,
            TotalMembers = members.Count,
            AcceptedCount = accepted,
            RejectedCount = rejected,
            DeferredCount = deferred,
            WaitingCount = waiting,
            ExportedCount = members.Count(m =>
                string.Equals(m.ExportStatus, FinalDecisionService.ExportStatusExported, StringComparison.OrdinalIgnoreCase)),
            UnexportedCount = members.Count(m =>
                string.Equals(m.ExportStatus, FinalDecisionService.ExportStatusUnexported, StringComparison.OrdinalIgnoreCase)),
            CompleteExamsCount = complete,
            IncompleteExamsCount = incomplete,
            DeferredByAction = deferredBreakdown
        };
    }

    private async Task<CentralCompareCenterDto> BuildCompareRowAsync(
        MedicalCenter center,
        DateRange range,
        CentralCompareQuery query,
        CancellationToken cancellationToken)
    {
        var kpi = await BuildCenterKpiAsync(
            center, range, query.ResultStatusId, query.ExportStatus, clinicId: null, cancellationToken);

        var membersQ = MembersInCenter(center.CenterId, range);
        if (!string.IsNullOrWhiteSpace(query.ExportStatus))
            membersQ = membersQ.Where(m => m.ExportStatus == query.ExportStatus);

        var memberIds = await membersQ.Select(m => m.MemberId).ToListAsync(cancellationToken);
        var archivedCount = 0;
        if (memberIds.Count > 0)
        {
            archivedCount = await _archives.Query(asNoTracking: true)
                .CountAsync(a => a.CenterId == center.CenterId && memberIds.Contains(a.MemberId), cancellationToken);
        }

        if (query.IsArchived == true)
        {
            // صف المقارنة يظل يعرض الأعداد بعد تطبيق فلتر الأرشفة على الإجمالي.
            var archivedIds = await _archives.Query(asNoTracking: true)
                .Where(a => a.CenterId == center.CenterId && memberIds.Contains(a.MemberId))
                .Select(a => a.MemberId)
                .ToListAsync(cancellationToken);
            memberIds = memberIds.Intersect(archivedIds).ToList();
        }
        else if (query.IsArchived == false)
        {
            var archivedIds = await _archives.Query(asNoTracking: true)
                .Where(a => a.CenterId == center.CenterId)
                .Select(a => a.MemberId)
                .ToListAsync(cancellationToken);
            memberIds = memberIds.Except(archivedIds).ToList();
        }

        return new CentralCompareCenterDto
        {
            CenterId = center.CenterId,
            CenterCode = center.Code,
            CenterNameAr = center.NameAr,
            TotalMembers = query.IsArchived.HasValue || !string.IsNullOrWhiteSpace(query.ExportStatus)
                ? memberIds.Count
                : kpi.TotalMembers,
            AcceptedCount = kpi.AcceptedCount,
            RejectedCount = kpi.RejectedCount,
            DeferredCount = kpi.DeferredCount,
            WaitingCount = kpi.WaitingCount,
            ExportedCount = kpi.ExportedCount,
            UnexportedCount = kpi.UnexportedCount,
            ArchivedCount = archivedCount
        };
    }

    private static CenterKpiDto SumKpis(IReadOnlyList<CenterKpiDto> centers)
    {
        return new CenterKpiDto
        {
            CenterId = Guid.Empty,
            CenterCode = "TOTAL",
            CenterNameAr = "الإجمالي",
            TotalMembers = centers.Sum(c => c.TotalMembers),
            AcceptedCount = centers.Sum(c => c.AcceptedCount),
            RejectedCount = centers.Sum(c => c.RejectedCount),
            DeferredCount = centers.Sum(c => c.DeferredCount),
            WaitingCount = centers.Sum(c => c.WaitingCount),
            ExportedCount = centers.Sum(c => c.ExportedCount),
            UnexportedCount = centers.Sum(c => c.UnexportedCount),
            CompleteExamsCount = centers.Sum(c => c.CompleteExamsCount),
            IncompleteExamsCount = centers.Sum(c => c.IncompleteExamsCount),
            DeferredByAction = Array.Empty<DeferredActionBreakdownDto>()
        };
    }

    private static CenterKpiExcelRowDto ToKpiExcel(CenterKpiDto c) => new()
    {
        CenterCode = c.CenterCode,
        CenterNameAr = c.CenterNameAr,
        TotalMembers = c.TotalMembers,
        AcceptedCount = c.AcceptedCount,
        RejectedCount = c.RejectedCount,
        DeferredCount = c.DeferredCount,
        WaitingCount = c.WaitingCount,
        ExportedCount = c.ExportedCount,
        UnexportedCount = c.UnexportedCount
    };

    #endregion

    #region Detail builders

    private async Task<List<FinalMemberListItemDto>> BuildFinalMemberItemsAsync(
        Guid centerId,
        FinalMembersQuery query,
        DateRange range,
        CancellationToken cancellationToken)
    {
        // الفترة على DecidedAt لقرار نفس المركز + المنتسب ما زال في المركز الحالي.
        var hasPeriod = range.From.HasValue || range.ToExclusive.HasValue;
        Dictionary<Guid, FinalDecision> decisionByMember;
        List<Member> members;

        if (hasPeriod)
        {
            var decisionsQ = FinalDecisionsInCenter(centerId, range);
            if (query.ResultStatusId.HasValue)
                decisionsQ = decisionsQ.Where(d => d.ResultStatusId == query.ResultStatusId.Value);

            var decisionsInPeriod = await decisionsQ.ToListAsync(cancellationToken);
            decisionByMember = decisionsInPeriod
                .GroupBy(d => d.MemberId)
                .ToDictionary(g => g.Key, g => g.First());
            var decidedIds = decisionByMember.Keys.ToList();

            var membersQ = MembersCurrentlyInCenter(centerId)
                .Where(m => decidedIds.Contains(m.MemberId));
            if (!string.IsNullOrWhiteSpace(query.ExportStatus))
                membersQ = membersQ.Where(m => m.ExportStatus == query.ExportStatus);
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var s = query.Search.Trim();
                membersQ = membersQ.Where(m =>
                    m.FullName.Contains(s)
                    || m.RecruitmentNumber.Contains(s)
                    || m.FileNumber.ToString() == s);
            }

            members = await membersQ
                .OrderByDescending(m => m.UpdatedAt)
                .ToListAsync(cancellationToken);
            var keep = members.Select(m => m.MemberId).ToHashSet();
            decisionByMember = decisionByMember
                .Where(kv => keep.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        else
        {
            var membersQ = MembersCurrentlyInCenter(centerId);
            if (!string.IsNullOrWhiteSpace(query.ExportStatus))
                membersQ = membersQ.Where(m => m.ExportStatus == query.ExportStatus);
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var s = query.Search.Trim();
                membersQ = membersQ.Where(m =>
                    m.FullName.Contains(s)
                    || m.RecruitmentNumber.Contains(s)
                    || m.FileNumber.ToString() == s);
            }

            members = await membersQ
                .OrderByDescending(m => m.UpdatedAt)
                .ToListAsync(cancellationToken);

            var seedIds = members.Select(m => m.MemberId).ToList();
            var decisions = await _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
                .Where(d => d.CenterId == centerId && seedIds.Contains(d.MemberId))
                .ToListAsync(cancellationToken);
            decisionByMember = decisions
                .GroupBy(d => d.MemberId)
                .ToDictionary(g => g.Key, g => g.First());

            if (query.ResultStatusId.HasValue)
            {
                members = members
                    .Where(m => decisionByMember.TryGetValue(m.MemberId, out var d)
                                && d.ResultStatusId == query.ResultStatusId.Value)
                    .ToList();
                var keep = members.Select(m => m.MemberId).ToHashSet();
                decisionByMember = decisionByMember
                    .Where(kv => keep.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }
        }

        var memberIds = members.Select(m => m.MemberId).ToList();

        var assessments = await (
            from a in _clinicWorkspace.QueryClinicAssessments(asNoTracking: true)
            join e in _clinicWorkspace.QueryMedicalExams(asNoTracking: true) on a.ExamId equals e.ExamId
            where a.CenterId == centerId && memberIds.Contains(a.MemberId) && e.IsCurrent
            select new { a, e.ExaminerUserId }
        ).ToListAsync(cancellationToken);

        if (query.ClinicId.HasValue)
        {
            var clinicMembers = assessments
                .Where(x => x.a.ClinicId == query.ClinicId.Value)
                .Select(x => x.a.MemberId)
                .ToHashSet();
            members = members.Where(m => clinicMembers.Contains(m.MemberId)).ToList();
            memberIds = members.Select(m => m.MemberId).ToList();
        }

        var archives = await _archives.Query(asNoTracking: true)
            .Where(a => memberIds.Contains(a.MemberId))
            .ToListAsync(cancellationToken);
        var archiveByMember = archives.ToDictionary(a => a.MemberId);

        if (query.IsArchived == true)
            members = members.Where(m => archiveByMember.ContainsKey(m.MemberId)).ToList();
        else if (query.IsArchived == false)
            members = members.Where(m => !archiveByMember.ContainsKey(m.MemberId)).ToList();

        var deferred = await _clinicWorkspace.QueryDeferredActions(asNoTracking: true)
            .Where(d => d.CenterId == centerId
                        && memberIds.Contains(d.MemberId)
                        && d.Status == DeferredStatusRequested)
            .ToListAsync(cancellationToken);
        var deferredByMember = deferred
            .GroupBy(d => d.MemberId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.RequestedAt).First());

        var noteMap = new Dictionary<Guid, string?>();
        foreach (var id in memberIds)
        {
            var list = await _members.GetActiveNotesAsync(id, cancellationToken);
            noteMap[id] = list.FirstOrDefault()?.NoteText;
        }

        var clinics = await _referenceData.Clinics(asNoTracking: true).ToListAsync(cancellationToken);
        var clinicMap = clinics.ToDictionary(c => c.ClinicId);
        var results = await _referenceData.ResultStatuses(asNoTracking: true)
            .ToDictionaryAsync(r => r.StatusId, cancellationToken);
        var examinerIds = assessments.Select(a => a.ExaminerUserId).Distinct();
        var names = await _users.GetDisplayNamesByIdsAsync(examinerIds, cancellationToken);

        var actionTypes = await _referenceData.RequiredActionTypes(asNoTracking: true)
            .ToDictionaryAsync(a => a.ActionTypeId, cancellationToken);

        var items = new List<FinalMemberListItemDto>();
        foreach (var m in members)
        {
            decisionByMember.TryGetValue(m.MemberId, out var decision);
            archiveByMember.TryGetValue(m.MemberId, out var archive);
            deferredByMember.TryGetValue(m.MemberId, out var openDeferred);
            noteMap.TryGetValue(m.MemberId, out var note);

            string? deferredSummary = null;
            if (openDeferred is not null && actionTypes.TryGetValue(openDeferred.ActionTypeId, out var at))
                deferredSummary = $"{at.NameAr} — {openDeferred.Status}";

            var clinicRows = assessments
                .Where(x => x.a.MemberId == m.MemberId)
                .Select(x =>
                {
                    clinicMap.TryGetValue(x.a.ClinicId, out var clinic);
                    results.TryGetValue(x.a.ResultStatusId, out var rs);
                    names.TryGetValue(x.ExaminerUserId, out var display);
                    return new FinalMemberClinicSummaryDto
                    {
                        ClinicId = x.a.ClinicId,
                        ClinicCode = clinic?.Code ?? "?",
                        ClinicNameAr = clinic?.NameAr ?? "?",
                        ResultStatusId = x.a.ResultStatusId,
                        ResultNameAr = rs?.NameAr,
                        ExaminerUserId = x.ExaminerUserId,
                        ExaminerDisplayName = display,
                        AssessedAt = x.a.AssessedAt
                    };
                }).ToList();

            items.Add(new FinalMemberListItemDto
            {
                MemberId = m.MemberId,
                FileNumber = m.FileNumber,
                FullName = m.FullName,
                RecruitmentNumber = m.RecruitmentNumber,
                FinalResultStatusId = decision?.ResultStatusId,
                FinalResultNameAr = decision is not null && results.TryGetValue(decision.ResultStatusId, out var fr)
                    ? fr.NameAr : null,
                FinalResultReason = decision?.RejectionReason,
                DecidedAt = decision?.DecidedAt,
                DeferUntil = openDeferred?.DeferUntil,
                OpenDeferredActionSummary = deferredSummary,
                ExportStatus = m.ExportStatus,
                IsArchived = archive is not null,
                ArchiveNumber = archive?.ArchiveNumber,
                LatestGeneralNote = note,
                ClinicAssessments = clinicRows
            });
        }

        return items;
    }

    private async Task<List<DeferredReportItemDto>> LoadDeferredItemsAsync(
        Guid? centerId,
        DeferredReportQuery query,
        DateRange range,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var q = _clinicWorkspace.QueryDeferredActions(asNoTracking: true);
        if (centerId.HasValue)
            q = q.Where(d => d.CenterId == centerId.Value);
        if (range.From.HasValue)
            q = q.Where(d => d.RequestedAt >= range.From.Value);
        if (range.ToExclusive.HasValue)
            q = q.Where(d => d.RequestedAt < range.ToExclusive.Value);
        if (query.ActionTypeId.HasValue)
            q = q.Where(d => d.ActionTypeId == query.ActionTypeId.Value);
        if (!string.IsNullOrWhiteSpace(query.ActionStatus))
            q = q.Where(d => d.Status == query.ActionStatus);

        var rows = await (
            from d in q
            join m in _members.Query(asNoTracking: true) on d.MemberId equals m.MemberId
            join at in _referenceData.RequiredActionTypes(asNoTracking: true)
                on d.ActionTypeId equals at.ActionTypeId
            orderby d.RequestedAt descending
            select new { d, m, at }
        ).ToListAsync(cancellationToken);

        var items = rows.Select(x =>
        {
            var isLate = x.d.Status == DeferredStatusRequested
                         && x.d.DeferUntil.HasValue
                         && x.d.DeferUntil.Value < today;
            return new DeferredReportItemDto
            {
                DeferredActionId = x.d.DeferredActionId,
                MemberId = x.d.MemberId,
                FileNumber = x.m.FileNumber,
                FullName = x.m.FullName,
                CenterId = x.d.CenterId,
                ActionTypeId = x.d.ActionTypeId,
                ActionTypeCode = x.at.Code,
                ActionTypeNameAr = x.at.NameAr,
                Status = x.d.Status,
                DeferReason = x.d.DeferReason,
                ActionDescription = x.d.ActionDescription,
                DeferUntil = x.d.DeferUntil,
                RequestedAt = x.d.RequestedAt,
                RequestedByUserId = x.d.RequestedByUserId,
                IsLate = isLate,
                IsClinicLevel = x.d.ClinicId.HasValue,
                IsFinalDecisionLevel = x.d.FinalDecisionId.HasValue,
                ClinicId = x.d.ClinicId
            };
        }).ToList();

        if (query.LateOnly == true)
            items = items.Where(i => i.IsLate).ToList();

        return items;
    }

    private async Task<List<DeferredActionBreakdownDto>> BuildDeferredBreakdownAsync(
        Guid centerId,
        DateRange range,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = await LoadDeferredItemsAsync(
            centerId,
            new DeferredReportQuery { PageNumber = 1, PageSize = int.MaxValue },
            range,
            today,
            cancellationToken);

        return items
            .GroupBy(x => new { x.ActionTypeId, x.ActionTypeCode, x.ActionTypeNameAr, x.Status })
            .Select(g => new DeferredActionBreakdownDto
            {
                ActionTypeId = g.Key.ActionTypeId,
                ActionTypeCode = g.Key.ActionTypeCode,
                ActionTypeNameAr = g.Key.ActionTypeNameAr,
                Status = g.Key.Status,
                Count = g.Count(),
                LateCount = g.Count(x => x.IsLate)
            })
            .OrderBy(x => x.ActionTypeId)
            .ToList();
    }

    private async Task<List<ClinicResultCountDto>> BuildClinicBreakdownAsync(
        Guid centerId,
        DateRange range,
        Guid? clinicId,
        short? resultStatusId,
        CancellationToken cancellationToken)
    {
        var q = _clinicWorkspace.QueryClinicAssessments(asNoTracking: true)
            .Where(a => a.CenterId == centerId);
        if (range.From.HasValue)
            q = q.Where(a => a.AssessedAt >= range.From.Value);
        if (range.ToExclusive.HasValue)
            q = q.Where(a => a.AssessedAt < range.ToExclusive.Value);
        if (clinicId.HasValue)
            q = q.Where(a => a.ClinicId == clinicId.Value);
        if (resultStatusId.HasValue)
            q = q.Where(a => a.ResultStatusId == resultStatusId.Value);

        var grouped = await q
            .GroupBy(a => new { a.ClinicId, a.ResultStatusId })
            .Select(g => new { g.Key.ClinicId, g.Key.ResultStatusId, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var clinics = await _referenceData.Clinics(asNoTracking: true)
            .ToDictionaryAsync(c => c.ClinicId, cancellationToken);

        return grouped
            .GroupBy(x => x.ClinicId)
            .Select(g =>
            {
                clinics.TryGetValue(g.Key, out var clinic);
                return new ClinicResultCountDto
                {
                    ClinicId = g.Key,
                    ClinicCode = clinic?.Code ?? "?",
                    ClinicNameAr = clinic?.NameAr ?? "?",
                    AssessmentCount = g.Sum(x => x.Count),
                    AcceptedCount = g.Where(x => x.ResultStatusId == 1).Sum(x => x.Count),
                    RejectedCount = g.Where(x => x.ResultStatusId == 2).Sum(x => x.Count),
                    DeferredCount = g.Where(x => x.ResultStatusId == 3).Sum(x => x.Count)
                };
            })
            .OrderBy(x => x.ClinicCode)
            .ToList();
    }

    private async Task<List<DoctorLinkageByMemberItemDto>> BuildDoctorByMemberAsync(
        Guid centerId,
        DoctorLinkageQuery query,
        DateRange range,
        CancellationToken cancellationToken)
    {
        var examsQ = _clinicWorkspace.QueryMedicalExams(asNoTracking: true)
            .Where(e => e.CenterId == centerId && e.IsCurrent);
        if (range.From.HasValue)
            examsQ = examsQ.Where(e => e.ExaminedAt >= range.From.Value);
        if (range.ToExclusive.HasValue)
            examsQ = examsQ.Where(e => e.ExaminedAt < range.ToExclusive.Value);
        if (query.ClinicId.HasValue)
            examsQ = examsQ.Where(e => e.ClinicId == query.ClinicId.Value);

        var examRows = await (
            from e in examsQ
            join m in _members.Query(asNoTracking: true) on e.MemberId equals m.MemberId
            where m.CurrentMedicalCenterId == centerId
            select new { e, m }
        ).ToListAsync(cancellationToken);

        var examIds = examRows.Select(r => r.e.ExamId).ToList();
        var assessments = await _clinicWorkspace.QueryClinicAssessments(asNoTracking: true)
            .Where(a => examIds.Contains(a.ExamId))
            .ToDictionaryAsync(a => a.ExamId, cancellationToken);

        if (query.ResultStatusId.HasValue)
        {
            examRows = examRows
                .Where(r => assessments.TryGetValue(r.e.ExamId, out var a)
                            && a.ResultStatusId == query.ResultStatusId.Value)
                .ToList();
        }

        var memberIds = examRows.Select(r => r.m.MemberId).Distinct().ToList();
        var finalDecisions = await _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
            .Where(d => d.CenterId == centerId && memberIds.Contains(d.MemberId))
            .ToListAsync(cancellationToken);
        var finalByMember = finalDecisions
            .GroupBy(d => d.MemberId)
            .ToDictionary(g => g.Key, g => g.First());

        var clinics = await _referenceData.Clinics(asNoTracking: true)
            .ToDictionaryAsync(c => c.ClinicId, cancellationToken);
        var results = await _referenceData.ResultStatuses(asNoTracking: true)
            .ToDictionaryAsync(r => r.StatusId, cancellationToken);
        var names = await _users.GetDisplayNamesByIdsAsync(
            examRows.Select(r => r.e.ExaminerUserId).Distinct(), cancellationToken);

        return examRows
            .GroupBy(r => new { r.m.MemberId, r.m.FileNumber, r.m.FullName })
            .OrderBy(g => g.Key.FileNumber)
            .Select(g =>
            {
                finalByMember.TryGetValue(g.Key.MemberId, out var final);
                string? finalName = null;
                if (final is not null && results.TryGetValue(final.ResultStatusId, out var fr))
                    finalName = fr.NameAr;
                return new DoctorLinkageByMemberItemDto
                {
                    MemberId = g.Key.MemberId,
                    FileNumber = g.Key.FileNumber,
                    FullName = g.Key.FullName,
                    FinalResultStatusId = final?.ResultStatusId,
                    FinalResultNameAr = finalName,
                    DecidedAt = final?.DecidedAt,
                    Exams = g.Select(x =>
                    {
                        clinics.TryGetValue(x.e.ClinicId, out var clinic);
                        assessments.TryGetValue(x.e.ExamId, out var assessment);
                        short? rsId = assessment?.ResultStatusId;
                        string? rsName = null;
                        if (rsId.HasValue && results.TryGetValue(rsId.Value, out var rs))
                            rsName = rs.NameAr;
                        names.TryGetValue(x.e.ExaminerUserId, out var display);
                        return new DoctorExamLinkDto
                        {
                            ExaminerUserId = x.e.ExaminerUserId,
                            ExaminerDisplayName = display,
                            ClinicId = x.e.ClinicId,
                            ClinicCode = clinic?.Code ?? "?",
                            ClinicNameAr = clinic?.NameAr ?? "?",
                            ExaminedAt = x.e.ExaminedAt,
                            ResultStatusId = rsId,
                            ResultNameAr = rsName
                        };
                    }).OrderBy(e => e.ExaminedAt).ToList()
                };
            }).ToList();
    }

    private async Task<List<DoctorLinkageByDoctorItemDto>> BuildDoctorByDoctorAsync(
        Guid centerId,
        DoctorLinkageQuery query,
        DateRange range,
        CancellationToken cancellationToken)
    {
        var byMember = await BuildDoctorByMemberAsync(centerId, query, range, cancellationToken);
        var flat = byMember.SelectMany(m => m.Exams.Select(e => new { m.MemberId, e })).ToList();
        var clinics = await _referenceData.Clinics(asNoTracking: true)
            .ToDictionaryAsync(c => c.ClinicId, cancellationToken);
        var names = await _users.GetDisplayNamesByIdsAsync(
            flat.Select(x => x.e.ExaminerUserId).Distinct(), cancellationToken);

        return flat
            .GroupBy(x => new { x.e.ExaminerUserId, x.e.ClinicId })
            .Select(g =>
            {
                clinics.TryGetValue(g.Key.ClinicId, out var clinic);
                names.TryGetValue(g.Key.ExaminerUserId, out var display);
                return new DoctorLinkageByDoctorItemDto
                {
                    ExaminerUserId = g.Key.ExaminerUserId,
                    ExaminerDisplayName = display,
                    ClinicId = g.Key.ClinicId,
                    ClinicCode = clinic?.Code,
                    ClinicNameAr = clinic?.NameAr,
                    MemberCount = g.Select(x => x.MemberId).Distinct().Count(),
                    ExamCount = g.Count(),
                    AcceptedCount = g.Count(x => x.e.ResultStatusId == 1),
                    RejectedCount = g.Count(x => x.e.ResultStatusId == 2),
                    DeferredCount = g.Count(x => x.e.ResultStatusId == 3)
                };
            })
            .OrderByDescending(x => x.MemberCount)
            .ToList();
    }

    #endregion

    #region Shared helpers

    /// <summary>منتسبو المركز الحالي فقط (BR-059 مؤقت).</summary>
    private IQueryable<Member> MembersCurrentlyInCenter(Guid centerId)
        => _members.Query(asNoTracking: true)
            .Where(m => m.CurrentMedicalCenterId == centerId);

    /// <summary>
    /// منتسبو المركز بلا قرار نهائي لنفس المركز؛ الفترة على CreatedAt (تطابق waitingCount).
    /// </summary>
    private IQueryable<Member> QueryWaitingMembersInCenter(Guid centerId, DateRange range)
    {
        var decidedIds = _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
            .Where(d => d.CenterId == centerId)
            .Select(d => d.MemberId);
        return ApplyCreatedAtRange(
            MembersCurrentlyInCenter(centerId).Where(m => !decidedIds.Contains(m.MemberId)),
            range);
    }

    /// <summary>استعلام قائمة الانتظار بعد فلاتر التصدير/البحث/العيادة/الأرشيف.</summary>
    private IQueryable<Member> BuildWaitingMembersBaseQuery(
        Guid centerId,
        WaitingMembersQuery query,
        DateRange range)
    {
        var q = QueryWaitingMembersInCenter(centerId, range);

        if (!string.IsNullOrWhiteSpace(query.ExportStatus))
            q = q.Where(m => m.ExportStatus == query.ExportStatus);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(m =>
                m.FullName.Contains(s)
                || m.RecruitmentNumber.Contains(s)
                || m.FileNumber.ToString() == s);
        }

        if (query.ClinicId.HasValue)
        {
            var clinicId = query.ClinicId.Value;
            var inClinic = _clinicWorkspace.QueryClinicAssessments(asNoTracking: true)
                .Where(a => a.CenterId == centerId && a.ClinicId == clinicId)
                .Select(a => a.MemberId);
            q = q.Where(m => inClinic.Contains(m.MemberId));
        }

        if (query.IsArchived == true)
        {
            var archived = _archives.Query(asNoTracking: true).Select(a => a.MemberId);
            q = q.Where(m => archived.Contains(m.MemberId));
        }
        else if (query.IsArchived == false)
        {
            var archived = _archives.Query(asNoTracking: true).Select(a => a.MemberId);
            q = q.Where(m => !archived.Contains(m.MemberId));
        }

        return q;
    }

    /// <summary>إثراء صفوف قيد الانتظار بنفس شكل القائمة النهائية (بدون قرار نهائي).</summary>
    private async Task<List<FinalMemberListItemDto>> EnrichWaitingMemberItemsAsync(
        Guid centerId,
        List<Member> members,
        CancellationToken cancellationToken)
    {
        var memberIds = members.Select(m => m.MemberId).ToList();
        if (memberIds.Count == 0)
            return new List<FinalMemberListItemDto>();

        var assessments = await (
            from a in _clinicWorkspace.QueryClinicAssessments(asNoTracking: true)
            join e in _clinicWorkspace.QueryMedicalExams(asNoTracking: true) on a.ExamId equals e.ExamId
            where a.CenterId == centerId && memberIds.Contains(a.MemberId) && e.IsCurrent
            select new { a, e.ExaminerUserId }
        ).ToListAsync(cancellationToken);

        var archives = await _archives.Query(asNoTracking: true)
            .Where(a => memberIds.Contains(a.MemberId))
            .ToListAsync(cancellationToken);
        var archiveByMember = archives.ToDictionary(a => a.MemberId);

        var deferred = await _clinicWorkspace.QueryDeferredActions(asNoTracking: true)
            .Where(d => d.CenterId == centerId
                        && memberIds.Contains(d.MemberId)
                        && d.Status == DeferredStatusRequested)
            .ToListAsync(cancellationToken);
        var deferredByMember = deferred
            .GroupBy(d => d.MemberId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.RequestedAt).First());

        var noteMap = new Dictionary<Guid, string?>();
        foreach (var id in memberIds)
        {
            var list = await _members.GetActiveNotesAsync(id, cancellationToken);
            noteMap[id] = list.FirstOrDefault()?.NoteText;
        }

        var clinics = await _referenceData.Clinics(asNoTracking: true).ToListAsync(cancellationToken);
        var clinicMap = clinics.ToDictionary(c => c.ClinicId);
        var results = await _referenceData.ResultStatuses(asNoTracking: true)
            .ToDictionaryAsync(r => r.StatusId, cancellationToken);
        var examinerIds = assessments.Select(a => a.ExaminerUserId).Distinct();
        var names = await _users.GetDisplayNamesByIdsAsync(examinerIds, cancellationToken);
        var actionTypes = await _referenceData.RequiredActionTypes(asNoTracking: true)
            .ToDictionaryAsync(a => a.ActionTypeId, cancellationToken);

        var items = new List<FinalMemberListItemDto>(members.Count);
        foreach (var m in members)
        {
            archiveByMember.TryGetValue(m.MemberId, out var archive);
            deferredByMember.TryGetValue(m.MemberId, out var openDeferred);
            noteMap.TryGetValue(m.MemberId, out var note);

            string? deferredSummary = null;
            if (openDeferred is not null && actionTypes.TryGetValue(openDeferred.ActionTypeId, out var at))
                deferredSummary = $"{at.NameAr} — {openDeferred.Status}";

            var clinicRows = assessments
                .Where(x => x.a.MemberId == m.MemberId)
                .Select(x =>
                {
                    clinicMap.TryGetValue(x.a.ClinicId, out var clinic);
                    results.TryGetValue(x.a.ResultStatusId, out var rs);
                    names.TryGetValue(x.ExaminerUserId, out var display);
                    return new FinalMemberClinicSummaryDto
                    {
                        ClinicId = x.a.ClinicId,
                        ClinicCode = clinic?.Code ?? "?",
                        ClinicNameAr = clinic?.NameAr ?? "?",
                        ResultStatusId = x.a.ResultStatusId,
                        ResultNameAr = rs?.NameAr,
                        ExaminerUserId = x.ExaminerUserId,
                        ExaminerDisplayName = display,
                        AssessedAt = x.a.AssessedAt
                    };
                }).ToList();

            items.Add(new FinalMemberListItemDto
            {
                MemberId = m.MemberId,
                FileNumber = m.FileNumber,
                FullName = m.FullName,
                RecruitmentNumber = m.RecruitmentNumber,
                FinalResultStatusId = null,
                FinalResultNameAr = null,
                FinalResultReason = null,
                DecidedAt = null,
                DeferUntil = openDeferred?.DeferUntil,
                OpenDeferredActionSummary = deferredSummary,
                ExportStatus = m.ExportStatus,
                IsArchived = archive is not null,
                ArchiveNumber = archive?.ArchiveNumber,
                LatestGeneralNote = note,
                ClinicAssessments = clinicRows
            });
        }

        return items;
    }

    /// <summary>تصفية تسجيل المنتسب حسب CreatedAt ضمن المدى الشامل للطرفين.</summary>
    private static IQueryable<Member> ApplyCreatedAtRange(IQueryable<Member> query, DateRange range)
    {
        if (range.From.HasValue)
            query = query.Where(m => m.CreatedAt >= range.From.Value);
        if (range.ToExclusive.HasValue)
            query = query.Where(m => m.CreatedAt < range.ToExclusive.Value);
        return query;
    }

    /// <summary>قرارات نهائية لنفس المركز ضمن DecidedAt (حدود UTC ليوم التقويم).</summary>
    private IQueryable<FinalDecision> FinalDecisionsInCenter(Guid centerId, DateRange range)
    {
        var q = _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
            .Where(d => d.CenterId == centerId);
        if (range.From.HasValue)
            q = q.Where(d => d.DecidedAt >= range.From.Value);
        if (range.ToExclusive.HasValue)
            q = q.Where(d => d.DecidedAt < range.ToExclusive.Value);
        return q;
    }

    /// <summary>منتسبو المركز مع فلتر CreatedAt — للمقارنة المركزية/مسارات التسجيل فقط.</summary>
    private IQueryable<Member> MembersInCenter(Guid centerId, DateRange range)
        => ApplyCreatedAtRange(MembersCurrentlyInCenter(centerId), range);

    private async Task<IReadOnlyList<(Guid ClinicId, string Code)>> LoadMandatoryAsync(
        Guid centerId,
        CancellationToken cancellationToken)
    {
        var links = await _centerClinics.Query(asNoTracking: true)
            .Where(cc => cc.CenterId == centerId && cc.IsActive)
            .Select(cc => cc.ClinicId)
            .ToListAsync(cancellationToken);
        var clinics = await _referenceData.Clinics(asNoTracking: true)
            .Where(c => c.IsActive && c.CountsForFinal && links.Contains(c.ClinicId))
            .Select(c => new ValueTuple<Guid, string>(c.ClinicId, c.Code))
            .ToListAsync(cancellationToken);
        return clinics;
    }

    private async Task<List<MedicalCenter>> ResolveCentersAsync(
        IReadOnlyList<Guid>? centerIds,
        CancellationToken cancellationToken)
    {
        var q = _centers.Query(asNoTracking: true).Where(c => c.IsActive);
        if (centerIds is { Count: > 0 })
        {
            var ids = centerIds.Where(id => id != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0)
                throw new InvalidOperationalReportFilterException("قائمة المراكز غير صالحة.");
            q = q.Where(c => ids.Contains(c.CenterId));
        }

        var list = await q.OrderBy(c => c.Code).ToListAsync(cancellationToken);
        if (list.Count == 0)
            throw new InvalidOperationalReportFilterException("لا توجد مراكز مطابقة للتصفية.");
        return list;
    }

    private async Task EnsureNoOrphanMembersAsync(CancellationToken cancellationToken)
    {
        // المخطط يفرض FK؛ نفحص السجلات ذات CenterId فارغ كحماية دفاعية لـ FR-063 A2.
        var orphan = await _members.Query(asNoTracking: true)
            .CountAsync(m => m.CurrentMedicalCenterId == Guid.Empty, cancellationToken);
        if (orphan > 0)
            throw new OperationalReportUnlinkedCenterException(orphan);
    }

    private async Task<MedicalCenter> RequireCenterAsync(Guid centerId, CancellationToken cancellationToken)
        => await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken)
           ?? throw new InvalidOperationalReportFilterException("المركز النشط غير موجود.");

    private async Task RecordCentralAccessAsync(
        Guid targetCenterId,
        string reason,
        string dataType,
        string? criteria,
        CancellationToken cancellationToken)
    {
        try
        {
            await _crossCenterAccess.RecordAsync(
                targetCenterId, reason, dataType, criteria,
                entityType: "OperationalReport",
                entityId: targetCenterId.ToString(),
                cancellationToken);
        }
        catch (InvalidUserDataException)
        {
            // الاطلاع ضمن نفس المركز النشط ليس عابراً — يُتجاهل بهدوء.
        }
    }

    private async Task<ExportFileDto> ExportRowsAsync<T>(
        IReadOnlyList<T> rows,
        string reportName,
        string sheetName,
        string title,
        CancellationToken cancellationToken,
        Guid? centerId = null)
    {
        var file = await _export.ExportToExcelAsync(rows, new ExportOptions
        {
            ReportName = reportName,
            SheetName = sheetName,
            CustomTitle = title,
            IncludeSummary = false
        });

        var userId = RequireUserId();
        RecordAudit(ExportEventType, "OperationalReport", centerId?.ToString(),
            title, new { RecordCount = rows.Count, reportName });
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return file;
    }

    private void RecordAudit(string eventType, string entityType, string? entityId, string reason, object newValues)
    {
        var userId = string.IsNullOrWhiteSpace(_currentUser.UserId) ? null : _currentUser.UserId;
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Central,
            CenterId = null,
            EventType = eventType,
            Result = "SUCCESS",
            EntityType = entityType,
            EntityId = entityId,
            ReasonText = reason,
            NewValues = JsonSerializer.Serialize(newValues),
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });
    }

    private string RequireUserId()
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
            throw new UnauthorizedAccessException("المستخدم غير مصادق.");
        return _currentUser.UserId;
    }

    private static DateRange NormalizeRange(OperationalDateRangeQuery query)
    {
        if (query.Date.HasValue && (query.FromDate.HasValue || query.ToDate.HasValue))
            throw new InvalidOperationalReportFilterException("لا تجمع بين تاريخ محدد ومجال زمني.");

        DateOnly? from = query.Date ?? query.FromDate;
        DateOnly? to = query.Date ?? query.ToDate;
        if (from.HasValue && to.HasValue && from > to)
            throw new InvalidOperationalReportFilterException("تاريخ البداية بعد تاريخ النهاية.");

        DateTimeOffset? fromOffset = from.HasValue
            ? new DateTimeOffset(from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero)
            : null;
        DateTimeOffset? toExclusive = to.HasValue
            ? new DateTimeOffset(to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero)
            : null;

        return new DateRange(fromOffset, toExclusive, from, to);
    }

    private static void NormalizePaging(FinalMembersQuery query)
    {
        if (query.PageNumber < 1) query.PageNumber = 1;
        if (query.PageSize < 1) query.PageSize = 20;
        if (query.PageSize > MaxPageSize) query.PageSize = MaxPageSize;
    }

    private static void NormalizePaging(WaitingMembersQuery query)
    {
        if (query.PageNumber < 1) query.PageNumber = 1;
        if (query.PageSize < 1) query.PageSize = 20;
        if (query.PageSize > MaxPageSize) query.PageSize = MaxPageSize;
    }

    private static void NormalizeDeferredQuery(DeferredReportQuery query)
    {
        if (query.PageNumber < 1) query.PageNumber = 1;
        if (query.PageSize < 1) query.PageSize = 20;
        if (query.PageSize > MaxPageSize) query.PageSize = MaxPageSize;
        if (!string.IsNullOrWhiteSpace(query.ActionStatus))
        {
            var s = query.ActionStatus.Trim().ToUpperInvariant();
            if (s is not DeferredStatusRequested and not DeferredStatusCompleted and not DeferredStatusClosed)
                throw new InvalidOperationalReportFilterException("حالة الإجراء المؤجل غير صالحة.");
            query.ActionStatus = s;
        }
    }

    private static void NormalizeDoctorQuery(DoctorLinkageQuery query)
    {
        if (query.PageNumber < 1) query.PageNumber = 1;
        if (query.PageSize < 1) query.PageSize = 20;
        if (query.PageSize > MaxPageSize) query.PageSize = MaxPageSize;
        var mode = (query.ViewMode ?? DoctorLinkageViewModes.ByMember).Trim().ToUpperInvariant();
        if (mode is not DoctorLinkageViewModes.ByMember and not DoctorLinkageViewModes.ByDoctor)
            throw new InvalidOperationalReportFilterException("وضع العرض يجب أن يكون BY_MEMBER أو BY_DOCTOR.");
        query.ViewMode = mode;
    }

    private static void NormalizeExportStatus(FinalMembersQuery query)
        => query.ExportStatus = NormalizeExportStatusValue(query.ExportStatus);

    private static void NormalizeExportStatus(WaitingMembersQuery query)
        => query.ExportStatus = NormalizeExportStatusValue(query.ExportStatus);

    private static void NormalizeExportStatus(CentralCompareQuery query)
        => query.ExportStatus = NormalizeExportStatusValue(query.ExportStatus);

    private static void NormalizeExportStatus(LocalKpiQuery query)
        => query.ExportStatus = NormalizeExportStatusValue(query.ExportStatus);

    private static string? NormalizeExportStatusValue(string? exportStatus)
    {
        if (string.IsNullOrWhiteSpace(exportStatus))
            return null;

        var s = exportStatus.Trim().ToUpperInvariant();
        if (s is not FinalDecisionService.ExportStatusExported
            and not FinalDecisionService.ExportStatusUnexported)
            throw new InvalidOperationalReportFilterException("حالة التصدير غير صالحة.");
        return s;
    }

    private static decimal Percent(int part, int total)
        => total <= 0 ? 0 : Math.Round(part * 100m / total, 2);

    private readonly record struct DateRange(
        DateTimeOffset? From,
        DateTimeOffset? ToExclusive,
        DateOnly? FromDate,
        DateOnly? ToDate);

    #endregion
}
