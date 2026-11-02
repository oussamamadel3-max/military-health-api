using System.Net;
using System.Text.Json;
using Application.Common.Permissions;
using Application.Common.Results;
using Application.Common.Security;
using Application.Dtos.Archive;
using Application.Dtos.ExportDtos;
using Application.Dtos.Members;
using Application.Dtos.Supervisor;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.Members;
using Application.Services.Supervisor;
using Domain.Entities.Archive;
using Domain.Entities.Audit;
using Domain.Entities.Clinical;
using Domain.Entities.Patient;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Archive;

/// <summary>
/// بحث الأرشيف والتقرير الطبي الكامل والطباعة العربية (FR-070…FR-072).
/// يعيد استخدام سجل <see cref="MemberArchive"/> من FR-069 دون إنشاء أرشيف ثانٍ.
/// </summary>
public class MemberArchiveService : IMemberArchiveService
{
    public const string ArchiveReportViewEventType = "ArchiveReportViewed";
    public const string ArchiveReportPrintEventType = "ArchiveReportPrinted";
    public const string CrossCenterDataType = "MemberArchive";

    private const int MaxPageSize = 200;
    private const string DeferredStatusRequested = "REQUESTED";
    private const string DeferredStatusClosed = "CLOSED";
    private const string IncompleteWithoutDecisionAr =
        "التقرير غير مكتمل: لا يتوفر قرار نهائي مرتبط بسجل الأرشيف.";

    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;
    private readonly IMemberArchiveRepository _archives;
    private readonly IMemberRepository _members;
    private readonly IClinicWorkspaceRepository _clinicWorkspace;
    private readonly ICenterClinicRepository _centerClinics;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IMedicalCenterRepository _centers;
    private readonly IUserRepository _users;
    private readonly IMemberMedicalRequestService _medicalRequests;
    private readonly ICrossCenterAccessService _crossCenterAccess;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public MemberArchiveService(
        IPermissionGate permissionGate,
        ICurrentUserService currentUser,
        IMemberArchiveRepository archives,
        IMemberRepository members,
        IClinicWorkspaceRepository clinicWorkspace,
        ICenterClinicRepository centerClinics,
        IReferenceDataRepository referenceData,
        IMedicalCenterRepository centers,
        IUserRepository users,
        IMemberMedicalRequestService medicalRequests,
        ICrossCenterAccessService crossCenterAccess,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _permissionGate = permissionGate;
        _currentUser = currentUser;
        _archives = archives;
        _members = members;
        _clinicWorkspace = clinicWorkspace;
        _centerClinics = centerClinics;
        _referenceData = referenceData;
        _centers = centers;
        _users = users;
        _medicalRequests = medicalRequests;
        _crossCenterAccess = crossCenterAccess;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public async Task<PaginatedList<ArchiveSearchItemDto>> SearchLocalAsync(
        ArchiveSearchQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Archive.ViewPrint,
            clientCenterId,
            cancellationToken);

        NormalizePaging(query);
        // المسار المحلي يتجاهل CenterId القادم من العميل ويلتزم بالمركز النشط.
        query.CenterId = null;

        return await ExecuteSearchAsync(query, centerId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PaginatedList<ArchiveSearchItemDto>> SearchCentralAsync(
        ArchiveSearchQuery query,
        string accessReason,
        CancellationToken cancellationToken = default)
    {
        await _permissionGate.EnsureCentralAsync(
            MilitaryHealthPermissions.Archive.ViewPrint,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(accessReason))
            throw new InvalidArchiveFilterException("سبب الاطلاع العابر مطلوب.");

        NormalizePaging(query);
        var page = await ExecuteSearchAsync(query, scopeCenterId: null, cancellationToken);

        var centersToAudit = page.Items.Select(i => i.CenterId).Distinct().ToList();
        if (centersToAudit.Count == 0 && query.CenterId.HasValue)
            centersToAudit.Add(query.CenterId.Value);

        var criteria = BuildSearchCriteria(query);
        foreach (var centerId in centersToAudit)
        {
            await _crossCenterAccess.RecordAsync(
                centerId,
                accessReason.Trim(),
                CrossCenterDataType,
                criteria,
                nameof(MemberArchive),
                entityId: null,
                cancellationToken);
        }

        return page;
    }

    /// <inheritdoc />
    public async Task<MemberFullMedicalReportDto> GetFullReportLocalAsync(
        Guid archiveId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Archive.ViewPrint,
            clientCenterId,
            cancellationToken);

        var report = await BuildReportAsync(archiveId, requiredCenterId: centerId, cancellationToken);
        await PersistViewAuditAsync(report, print: false, cancellationToken);
        return report;
    }

    /// <inheritdoc />
    public async Task<MemberFullMedicalReportDto> GetFullReportCentralAsync(
        Guid archiveId,
        string accessReason,
        CancellationToken cancellationToken = default)
    {
        await _permissionGate.EnsureCentralAsync(
            MilitaryHealthPermissions.Archive.ViewPrint,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(accessReason))
            throw new InvalidArchiveFilterException("سبب الاطلاع العابر مطلوب.");

        var report = await BuildReportAsync(archiveId, requiredCenterId: null, cancellationToken);

        await _crossCenterAccess.RecordAsync(
            report.ArchiveCenterId,
            accessReason.Trim(),
            CrossCenterDataType,
            JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["archiveId"] = archiveId.ToString("D"),
                ["archiveNumber"] = report.ArchiveNumber
            }),
            nameof(MemberArchive),
            archiveId.ToString("D"),
            cancellationToken);

        await PersistViewAuditAsync(report, print: false, cancellationToken);
        return report;
    }

    /// <inheritdoc />
    public async Task<ExportFileDto> PrintFullReportLocalAsync(
        Guid archiveId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Archive.ViewPrint,
            clientCenterId,
            cancellationToken);

        var report = await BuildReportAsync(archiveId, requiredCenterId: centerId, cancellationToken);
        var file = MemberArchiveReportPdfBuilder.Build(report, DateTimeOffset.UtcNow);
        await PersistViewAuditAsync(report, print: true, cancellationToken);
        return file;
    }

    /// <inheritdoc />
    public async Task<ExportFileDto> PrintFullReportCentralAsync(
        Guid archiveId,
        string accessReason,
        CancellationToken cancellationToken = default)
    {
        await _permissionGate.EnsureCentralAsync(
            MilitaryHealthPermissions.Archive.ViewPrint,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(accessReason))
            throw new InvalidArchiveFilterException("سبب الاطلاع العابر مطلوب.");

        var report = await BuildReportAsync(archiveId, requiredCenterId: null, cancellationToken);

        await _crossCenterAccess.RecordAsync(
            report.ArchiveCenterId,
            accessReason.Trim(),
            CrossCenterDataType,
            JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["archiveId"] = archiveId.ToString("D"),
                ["action"] = "print"
            }),
            nameof(MemberArchive),
            archiveId.ToString("D"),
            cancellationToken);

        var file = MemberArchiveReportPdfBuilder.Build(report, DateTimeOffset.UtcNow);
        await PersistViewAuditAsync(report, print: true, cancellationToken);
        return file;
    }

    #region Private Helpers

    private async Task<PaginatedList<ArchiveSearchItemDto>> ExecuteSearchAsync(
        ArchiveSearchQuery query,
        Guid? scopeCenterId,
        CancellationToken cancellationToken)
    {
        ValidateFilters(query);

        var archivesQ = _archives.Query(asNoTracking: true);
        if (scopeCenterId.HasValue)
            archivesQ = archivesQ.Where(a => a.CenterId == scopeCenterId.Value);
        else if (query.CenterId.HasValue)
            archivesQ = archivesQ.Where(a => a.CenterId == query.CenterId.Value);

        if (!string.IsNullOrWhiteSpace(query.ArchiveNumber))
        {
            var an = query.ArchiveNumber.Trim().ToLowerInvariant();
            archivesQ = archivesQ.Where(a => a.ArchiveNumber.ToLower().Contains(an));
        }

        if (query.ArchivedFrom.HasValue)
        {
            var from = ToUtcStart(query.ArchivedFrom.Value);
            archivesQ = archivesQ.Where(a => a.ArchivedAt >= from);
        }

        if (query.ArchivedTo.HasValue)
        {
            var to = ToUtcEnd(query.ArchivedTo.Value);
            archivesQ = archivesQ.Where(a => a.ArchivedAt <= to);
        }

        var membersQ = _members.Query(asNoTracking: true);
        if (query.FileNumber.HasValue)
            membersQ = membersQ.Where(m => m.FileNumber == query.FileNumber.Value);

        if (!string.IsNullOrWhiteSpace(query.FullName))
        {
            var name = query.FullName.Trim();
            membersQ = membersQ.Where(m => m.FullName.Contains(name));
        }

        if (!string.IsNullOrWhiteSpace(query.RecruitmentNumber))
        {
            var rn = query.RecruitmentNumber.Trim().ToLowerInvariant();
            membersQ = membersQ.Where(m => m.RecruitmentNumber.ToLower() == rn);
        }

        var decisionsQ = _clinicWorkspace.QueryFinalDecisions(asNoTracking: true);
        if (query.ResultStatusId.HasValue)
            decisionsQ = decisionsQ.Where(d => d.ResultStatusId == query.ResultStatusId.Value);

        if (query.DecisionFrom.HasValue)
        {
            var from = ToUtcStart(query.DecisionFrom.Value);
            decisionsQ = decisionsQ.Where(d => d.DecidedAt >= from);
        }

        if (query.DecisionTo.HasValue)
        {
            var to = ToUtcEnd(query.DecisionTo.Value);
            decisionsQ = decisionsQ.Where(d => d.DecidedAt <= to);
        }

        var joined =
            from a in archivesQ
            join m in membersQ on a.MemberId equals m.MemberId
            join d in decisionsQ on a.FinalDecisionId equals d.FinalDecisionId
            select new { Archive = a, Member = m, Decision = d };

        var total = await joined.CountAsync(cancellationToken);

        var rows = await joined
            .OrderByDescending(x => x.Archive.ArchivedAt)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new
            {
                x.Archive.ArchiveId,
                x.Archive.ArchiveNumber,
                x.Archive.ArchivedAt,
                x.Archive.CenterId,
                x.Member.MemberId,
                x.Member.FileNumber,
                x.Member.FullName,
                x.Member.RecruitmentNumber,
                x.Decision.FinalDecisionId,
                x.Decision.ResultStatusId,
                x.Decision.DecidedAt
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return new PaginatedList<ArchiveSearchItemDto>(
                Array.Empty<ArchiveSearchItemDto>(), total, query.PageNumber, query.PageSize);

        var centerIds = rows.Select(r => r.CenterId).Distinct().ToList();
        var centers = await _centers.Query(asNoTracking: true)
            .Where(c => centerIds.Contains(c.CenterId))
            .Select(c => new { c.CenterId, c.Code, c.NameAr })
            .ToListAsync(cancellationToken);
        var centerMap = centers.ToDictionary(c => c.CenterId);

        var resultStatuses = await _referenceData.ResultStatuses(asNoTracking: true)
            .Select(s => new { s.StatusId, s.NameAr })
            .ToListAsync(cancellationToken);
        var resultMap = resultStatuses.ToDictionary(s => s.StatusId, s => s.NameAr);

        var items = rows.Select(r =>
        {
            centerMap.TryGetValue(r.CenterId, out var center);
            resultMap.TryGetValue(r.ResultStatusId, out var resultName);
            return new ArchiveSearchItemDto
            {
                ArchiveId = r.ArchiveId,
                ArchiveNumber = r.ArchiveNumber,
                ArchivedAt = r.ArchivedAt,
                MemberId = r.MemberId,
                FileNumber = r.FileNumber,
                FullName = r.FullName,
                RecruitmentNumber = r.RecruitmentNumber,
                CenterId = r.CenterId,
                CenterCode = center?.Code,
                CenterNameAr = center?.NameAr,
                FinalDecisionId = r.FinalDecisionId,
                ResultStatusId = r.ResultStatusId,
                ResultStatusNameAr = resultName,
                DecidedAt = r.DecidedAt
            };
        }).ToList();

        return new PaginatedList<ArchiveSearchItemDto>(items, total, query.PageNumber, query.PageSize);
    }

    private async Task<MemberFullMedicalReportDto> BuildReportAsync(
        Guid archiveId,
        Guid? requiredCenterId,
        CancellationToken cancellationToken)
    {
        if (archiveId == Guid.Empty)
            throw new InvalidArchiveFilterException("معرّف الأرشيف مطلوب.");

        var archive = await _archives.Query(asNoTracking: true)
            .FirstOrDefaultAsync(a => a.ArchiveId == archiveId, cancellationToken);
        if (archive is null)
            throw new ArchiveNotFoundException();

        if (requiredCenterId.HasValue && archive.CenterId != requiredCenterId.Value)
            throw new ArchiveNotFoundException();

        var member = await _members.GetByIdAsync(archive.MemberId, asNoTracking: true, cancellationToken);
        if (member is null)
            throw new ArchiveNotFoundException();

        var decision = await _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
            .FirstOrDefaultAsync(d => d.FinalDecisionId == archive.FinalDecisionId, cancellationToken);

        var archiveCenter = await _centers.GetByIdAsync(archive.CenterId, asNoTracking: true, cancellationToken);
        var memberDto = await MapMemberDetailsAsync(member, cancellationToken);

        var medicalRequests = await _medicalRequests.GetForMemberInAuthorizedCenterAsync(
            archive.MemberId,
            archive.CenterId,
            cancellationToken);

        var clinicRows = await BuildClinicRowsAsync(
            archive.MemberId,
            archive.CenterId,
            cancellationToken);

        var deferredItems = await BuildDeferredItemsAsync(
            archive.MemberId,
            archive.CenterId,
            cancellationToken);

        IReadOnlyList<ArchiveDecisionHistoryItemDto> historyItems =
            Array.Empty<ArchiveDecisionHistoryItemDto>();
        ArchiveFinalDecisionSectionDto decisionSection;
        var isComplete = decision is not null;
        string? incompleteReason = isComplete ? null : IncompleteWithoutDecisionAr;

        if (decision is null)
        {
            decisionSection = new ArchiveFinalDecisionSectionDto
            {
                FinalDecisionId = archive.FinalDecisionId,
                ResultStatusId = 0,
                SupervisorUserId = string.Empty,
                DecidedAt = archive.ArchivedAt,
                UpdatedAt = archive.UpdatedAt
            };
        }
        else
        {
            var resultStatuses = await _referenceData.ResultStatuses(asNoTracking: true)
                .Select(s => new { s.StatusId, s.NameAr })
                .ToListAsync(cancellationToken);
            var resultMap = resultStatuses.ToDictionary(s => s.StatusId, s => s.NameAr);

            var history = await _clinicWorkspace.QueryFinalDecisionHistories(asNoTracking: true)
                .Where(h => h.FinalDecisionId == decision.FinalDecisionId)
                .OrderBy(h => h.ChangedAt)
                .ToListAsync(cancellationToken);

            var userIds = new List<string> { decision.SupervisorUserId };
            userIds.AddRange(history.Select(h => h.ChangedByUserId));
            var names = await _users.GetDisplayNamesByIdsAsync(
                userIds.Distinct(StringComparer.Ordinal).ToList(),
                cancellationToken);

            names.TryGetValue(decision.SupervisorUserId, out var supervisorName);
            resultMap.TryGetValue(decision.ResultStatusId, out var resultName);

            SupervisorDeferredActionItemDto? linkedDeferred = deferredItems
                .FirstOrDefault(d => d.FinalDecisionId == decision.FinalDecisionId);

            decisionSection = new ArchiveFinalDecisionSectionDto
            {
                FinalDecisionId = decision.FinalDecisionId,
                ResultStatusId = decision.ResultStatusId,
                ResultStatusNameAr = resultName,
                RejectionReason = decision.RejectionReason,
                DisagreementJustification = decision.DisagreementJustification,
                SupervisorUserId = decision.SupervisorUserId,
                SupervisorUserName = supervisorName,
                DecidedAt = decision.DecidedAt,
                UpdatedAt = decision.UpdatedAt,
                LinkedDeferredAction = linkedDeferred
            };

            historyItems = history.Select(h =>
            {
                resultMap.TryGetValue(h.OldStatusId, out var oldName);
                resultMap.TryGetValue(h.NewStatusId, out var newName);
                names.TryGetValue(h.ChangedByUserId, out var changedByName);
                return new ArchiveDecisionHistoryItemDto
                {
                    HistoryId = h.HistoryId,
                    OldStatusId = h.OldStatusId,
                    OldStatusNameAr = oldName,
                    NewStatusId = h.NewStatusId,
                    NewStatusNameAr = newName,
                    ChangeReason = h.ChangeReason,
                    ChangedByUserId = h.ChangedByUserId,
                    ChangedByUserName = changedByName,
                    ChangedAt = h.ChangedAt,
                    ExportResetPerformed = h.ExportResetPerformed
                };
            }).ToList();
        }

        return new MemberFullMedicalReportDto
        {
            ArchiveId = archive.ArchiveId,
            ArchiveNumber = archive.ArchiveNumber,
            ArchivedAt = archive.ArchivedAt,
            ArchiveUpdatedAt = archive.UpdatedAt,
            ArchiveCenterId = archive.CenterId,
            ArchiveCenterCode = archiveCenter?.Code,
            ArchiveCenterNameAr = archiveCenter?.NameAr,
            Member = memberDto,
            ClinicAssessments = clinicRows,
            MedicalRequests = medicalRequests,
            GeneralNotes = memberDto.GeneralNotes,
            DeferredActions = deferredItems,
            FinalDecision = decisionSection,
            DecisionHistory = historyItems,
            IsComplete = isComplete,
            IncompleteReasonAr = incompleteReason
        };
    }

    private async Task<List<SupervisorClinicAssessmentRowDto>> BuildClinicRowsAsync(
        Guid memberId,
        Guid centerId,
        CancellationToken cancellationToken)
    {
        var mandatoryClinics = await SupervisorMandatoryClinicLoader.LoadAsync(
            centerId,
            _centerClinics,
            _referenceData,
            cancellationToken);
        var clinicIds = mandatoryClinics.Select(c => c.ClinicId).ToList();
        if (clinicIds.Count == 0)
            return new List<SupervisorClinicAssessmentRowDto>();

        var currentExams = await _clinicWorkspace.QueryMedicalExams(asNoTracking: true)
            .Where(e => e.MemberId == memberId
                && e.CenterId == centerId
                && e.IsCurrent
                && clinicIds.Contains(e.ClinicId))
            .Select(e => new { e.ClinicId, e.ExamId })
            .ToListAsync(cancellationToken);
        var examByClinic = currentExams
            .GroupBy(e => e.ClinicId)
            .ToDictionary(g => g.Key, g => g.First());

        var assessments = await _clinicWorkspace.QueryClinicAssessments(asNoTracking: true)
            .Where(a => a.MemberId == memberId
                && a.CenterId == centerId
                && clinicIds.Contains(a.ClinicId))
            .ToListAsync(cancellationToken);
        var assessmentByClinic = assessments
            .GroupBy(a => a.ClinicId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AssessedAt).First());

        var openDeferred = await _clinicWorkspace.QueryDeferredActions(asNoTracking: true)
            .Where(d => d.MemberId == memberId
                && d.CenterId == centerId
                && d.Status == DeferredStatusRequested
                && d.ClinicAssessmentId != null)
            .ToListAsync(cancellationToken);
        var deferredByAssessment = openDeferred
            .GroupBy(d => d.ClinicAssessmentId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var resultStatuses = await _referenceData.ResultStatuses(asNoTracking: true)
            .Select(s => new { s.StatusId, s.NameAr })
            .ToListAsync(cancellationToken);
        var resultMap = resultStatuses.ToDictionary(s => s.StatusId, s => s.NameAr);

        var actionTypeIds = openDeferred.Select(d => d.ActionTypeId).Distinct().ToList();
        var actionTypes = actionTypeIds.Count == 0
            ? new Dictionary<short, string>()
            : await _referenceData.RequiredActionTypes(asNoTracking: true)
                .Where(t => actionTypeIds.Contains(t.ActionTypeId))
                .ToDictionaryAsync(t => t.ActionTypeId, t => t.NameAr, cancellationToken);

        var userIds = assessmentByClinic.Values.Select(a => a.AssessedByUserId).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await _users.GetDisplayNamesByIdsAsync(userIds, cancellationToken);

        var rows = new List<SupervisorClinicAssessmentRowDto>(mandatoryClinics.Count);
        foreach (var clinic in mandatoryClinics)
        {
            assessmentByClinic.TryGetValue(clinic.ClinicId, out var assessment);
            examByClinic.TryGetValue(clinic.ClinicId, out var exam);

            SupervisorClinicDeferredSummaryDto? deferredSummary = null;
            if (assessment is not null
                && deferredByAssessment.TryGetValue(assessment.AssessmentId, out var deferred))
            {
                actionTypes.TryGetValue(deferred.ActionTypeId, out var actionName);
                deferredSummary = new SupervisorClinicDeferredSummaryDto
                {
                    DeferredActionId = deferred.DeferredActionId,
                    DeferReason = deferred.DeferReason,
                    ActionTypeId = deferred.ActionTypeId,
                    ActionTypeNameAr = actionName,
                    ActionDescription = deferred.ActionDescription,
                    Status = deferred.Status,
                    DeferUntil = deferred.DeferUntil,
                    IsOpen = true
                };
            }

            string? doctorName = null;
            if (assessment is not null)
                names.TryGetValue(assessment.AssessedByUserId, out doctorName);

            string? resultName = null;
            if (assessment is not null)
                resultMap.TryGetValue(assessment.ResultStatusId, out resultName);

            rows.Add(new SupervisorClinicAssessmentRowDto
            {
                ClinicId = clinic.ClinicId,
                ClinicCode = clinic.Code,
                ClinicNameAr = clinic.NameAr,
                SortOrder = clinic.SortOrder,
                CountsForFinal = true,
                IsMissing = assessment is null,
                HasCurrentExam = exam is not null,
                ExamId = exam?.ExamId ?? assessment?.ExamId,
                AssessmentId = assessment?.AssessmentId,
                ResultStatusId = assessment?.ResultStatusId,
                ResultStatusNameAr = resultName,
                RejectionReason = assessment?.RejectionReason,
                ClinicalNotes = assessment?.ClinicalNotes,
                AssessedByUserId = assessment?.AssessedByUserId,
                AssessedByUserName = doctorName,
                AssessedAt = assessment?.AssessedAt,
                DeferredAction = deferredSummary
            });
        }

        return rows;
    }

    private async Task<List<SupervisorDeferredActionItemDto>> BuildDeferredItemsAsync(
        Guid memberId,
        Guid centerId,
        CancellationToken cancellationToken)
    {
        var deferredActions = await _clinicWorkspace.QueryDeferredActions(asNoTracking: true)
            .Where(d => d.MemberId == memberId
                && d.CenterId == centerId
                && d.Status != DeferredStatusClosed)
            .OrderByDescending(d => d.RequestedAt)
            .ToListAsync(cancellationToken);

        if (deferredActions.Count == 0)
            return new List<SupervisorDeferredActionItemDto>();

        var actionTypeIds = deferredActions.Select(d => d.ActionTypeId).Distinct().ToList();
        var actionTypes = await _referenceData.RequiredActionTypes(asNoTracking: true)
            .Where(t => actionTypeIds.Contains(t.ActionTypeId))
            .ToDictionaryAsync(t => t.ActionTypeId, t => t.NameAr, cancellationToken);

        var clinicIds = deferredActions.Where(d => d.ClinicId.HasValue)
            .Select(d => d.ClinicId!.Value)
            .Distinct()
            .ToList();
        var clinicNames = clinicIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _referenceData.Clinics(asNoTracking: true)
                .Where(c => clinicIds.Contains(c.ClinicId))
                .ToDictionaryAsync(c => c.ClinicId, c => c.NameAr, cancellationToken);

        var userIds = deferredActions.Select(d => d.RequestedByUserId).Distinct().ToList();
        var names = await _users.GetDisplayNamesByIdsAsync(userIds, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return deferredActions.Select(d =>
        {
            actionTypes.TryGetValue(d.ActionTypeId, out var actionName);
            names.TryGetValue(d.RequestedByUserId, out var requestedByName);
            string? clinicName = null;
            if (d.ClinicId.HasValue)
                clinicNames.TryGetValue(d.ClinicId.Value, out clinicName);

            return new SupervisorDeferredActionItemDto
            {
                DeferredActionId = d.DeferredActionId,
                ClinicId = d.ClinicId,
                ClinicNameAr = clinicName,
                ClinicAssessmentId = d.ClinicAssessmentId,
                FinalDecisionId = d.FinalDecisionId,
                DeferReason = d.DeferReason,
                ActionTypeId = d.ActionTypeId,
                ActionTypeNameAr = actionName,
                ActionDescription = d.ActionDescription,
                FollowupNote = d.FollowupNote,
                Status = d.Status,
                DeferUntil = d.DeferUntil,
                RequestedAt = d.RequestedAt,
                RequestedByUserId = d.RequestedByUserId,
                RequestedByUserName = requestedByName,
                CompletedByUserId = d.CompletedByUserId,
                CompletedAt = d.CompletedAt,
                IsOverdue = d.Status == DeferredStatusRequested
                    && d.DeferUntil != null
                    && d.DeferUntil < today
            };
        }).ToList();
    }

    private async Task<MemberDetailsDto> MapMemberDetailsAsync(
        Member member,
        CancellationToken cancellationToken)
    {
        var dto = _mapper.Map<MemberDetailsDto>(member);
        var bmi = member.Bmi ?? MemberBmi.Calculate(member.HeightCm, member.WeightKg);
        dto.Bmi = bmi;

        var categories = await _referenceData.BmiCategories(asNoTracking: true)
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(cancellationToken);
        dto.BmiCategoryNameAr = MemberBmi.Classify(bmi, categories);

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

    private async Task PersistViewAuditAsync(
        MemberFullMedicalReportDto report,
        bool print,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var now = DateTimeOffset.UtcNow;
        _audit.Add(new AuditEvent
        {
            OccurredAt = now,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = report.ArchiveCenterId,
            EventType = print ? ArchiveReportPrintEventType : ArchiveReportViewEventType,
            Result = "SUCCESS",
            EntityType = nameof(MemberArchive),
            EntityId = report.ArchiveId.ToString("D"),
            ReasonText = print
                ? $"طباعة التقرير الكامل للأرشيف {report.ArchiveNumber}"
                : $"عرض التقرير الكامل للأرشيف {report.ArchiveNumber}",
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null,
            NewValues = JsonSerializer.Serialize(new
            {
                report.ArchiveId,
                report.ArchiveNumber,
                report.Member.MemberId,
                report.Member.FileNumber,
                Print = print
            })
        });
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private string RequireUserId()
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId) || _currentUser.UserId == "0")
            throw new UnauthorizedAccessException("المستخدم غير مصادق.");
        return _currentUser.UserId;
    }

    private static void NormalizePaging(ArchiveSearchQuery query)
    {
        if (query.PageNumber < 1) query.PageNumber = 1;
        if (query.PageSize < 1) query.PageSize = 20;
        if (query.PageSize > MaxPageSize) query.PageSize = MaxPageSize;
    }

    private static void ValidateFilters(ArchiveSearchQuery query)
    {
        if (query.ResultStatusId is short rs
            && rs is not FinalDecisionService.AcceptedResultStatusId
            and not FinalDecisionService.RejectedResultStatusId
            and not FinalDecisionService.DeferredResultStatusId)
        {
            throw new InvalidArchiveFilterException("رمز نتيجة القرار غير صالح.");
        }

        if (query.DecisionFrom.HasValue
            && query.DecisionTo.HasValue
            && query.DecisionFrom > query.DecisionTo)
        {
            throw new InvalidArchiveFilterException("فترة القرار غير صالحة.");
        }

        if (query.ArchivedFrom.HasValue
            && query.ArchivedTo.HasValue
            && query.ArchivedFrom > query.ArchivedTo)
        {
            throw new InvalidArchiveFilterException("فترة الأرشفة غير صالحة.");
        }
    }

    private static string BuildSearchCriteria(ArchiveSearchQuery query)
        => JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["archiveNumber"] = query.ArchiveNumber,
            ["fileNumber"] = query.FileNumber?.ToString(),
            ["fullName"] = query.FullName,
            ["recruitmentNumber"] = query.RecruitmentNumber,
            ["centerId"] = query.CenterId?.ToString("D"),
            ["resultStatusId"] = query.ResultStatusId?.ToString()
        });

    private static DateTimeOffset ToUtcStart(DateOnly date)
        => new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero);

    private static DateTimeOffset ToUtcEnd(DateOnly date)
        => new(date.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc), TimeSpan.Zero);

    #endregion
}
