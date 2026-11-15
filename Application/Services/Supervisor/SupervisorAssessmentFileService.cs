using System.Net;
using System.Text.Json;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.Members;
using Application.Dtos.Supervisor;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.ClinicWorkspace;
using Application.Services.Members;
using Domain.Entities.Audit;
using Domain.Entities.Clinical;
using Domain.Entities.Patient;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Supervisor;

/// <summary>
/// تجهيز ملف التقييم الطبي المجمّع للمشرف ضمن المركز النشط (FR-051 / FR-052).
/// لا يستخدم قفل تحرير العيادة ولا يكرر عقود المرحلة الخامسة.
/// </summary>
public class SupervisorAssessmentFileService : ISupervisorAssessmentFileService
{
    public const string AssessmentFileViewEventType = "SupervisorAssessmentFileView";

    public const string IncompleteClinicsBlockReasonAr =
        "لا يمكن إصدار القرار النهائي قبل اكتمال تقييمات العيادات الإلزامية.";

    public const string ExistingDecisionBlockReasonAr =
        "يوجد قرار نهائي سابق لهذا المنتسب؛ يُعرض للتعديل وفق الصلاحية ولا يُنشأ قرار جديد.";

    public const string NoMandatoryClinicsBlockReasonAr =
        "لا توجد عيادات إلزامية مفعّلة في هذا المركز لإصدار القرار النهائي.";

    public const string PendingInvestigationWarningAr =
        "يوجد استقصاء معلّق؛ يُنصح بمراجعته قبل إصدار القرار النهائي.";

    public const string OpenDeferredWarningAr =
        "يوجد إجراء مؤجّل مفتوح لم يُنجز بعد.";

    private const string DeferredStatusRequested = "REQUESTED";
    private const string DeferredStatusClosed = "CLOSED";

    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;
    private readonly IMemberRepository _members;
    private readonly IClinicWorkspaceRepository _clinicWorkspace;
    private readonly ICenterClinicRepository _centerClinics;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IUserRepository _users;
    private readonly IMemberMedicalRequestService _medicalRequests;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly IMemberFieldPermissionGuard? _fieldPermissions;

    public SupervisorAssessmentFileService(
        IPermissionGate permissionGate,
        ICurrentUserService currentUser,
        IMemberRepository members,
        IClinicWorkspaceRepository clinicWorkspace,
        ICenterClinicRepository centerClinics,
        IReferenceDataRepository referenceData,
        IUserRepository users,
        IMemberMedicalRequestService medicalRequests,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        IMemberFieldPermissionGuard? fieldPermissions = null)
    {
        _permissionGate = permissionGate;
        _currentUser = currentUser;
        _members = members;
        _clinicWorkspace = clinicWorkspace;
        _centerClinics = centerClinics;
        _referenceData = referenceData;
        _users = users;
        _medicalRequests = medicalRequests;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _fieldPermissions = fieldPermissions;
    }

    /// <inheritdoc />
    public async Task<SupervisorAssessmentFileDto> GetAssessmentFileAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (memberId == Guid.Empty)
            throw new InvalidUserDataException("معرّف المنتسب مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Clinical.FinalDecision,
            clientCenterId,
            cancellationToken);

        var member = await _members.GetByIdInCenterAsync(
            memberId,
            centerId,
            asNoTracking: true,
            cancellationToken);
        if (member is null)
            throw new MemberNotFoundException();

        var memberDto = await MapMemberDetailsAsync(member, cancellationToken);
        var medicalRequests = await _medicalRequests.GetForMemberInAuthorizedCenterAsync(
            memberId,
            centerId,
            cancellationToken);

        var mandatoryClinics = await SupervisorMandatoryClinicLoader.LoadAsync(
            centerId,
            _centerClinics,
            _referenceData,
            cancellationToken);
        var clinicIds = mandatoryClinics.Select(c => c.ClinicId).ToList();

        var currentExams = clinicIds.Count == 0
            ? new List<ExamSnapshot>()
            : await _clinicWorkspace.QueryMedicalExams(asNoTracking: true)
                .Where(e => e.MemberId == memberId
                    && e.CenterId == centerId
                    && e.IsCurrent
                    && clinicIds.Contains(e.ClinicId))
                .Select(e => new ExamSnapshot(e.ClinicId, e.ExamId))
                .ToListAsync(cancellationToken);

        var examByClinic = currentExams
            .GroupBy(e => e.ClinicId)
            .ToDictionary(g => g.Key, g => g.First());

        var assessments = clinicIds.Count == 0
            ? new List<ClinicAssessment>()
            : await _clinicWorkspace.QueryClinicAssessments(asNoTracking: true)
                .Where(a => a.MemberId == memberId
                    && a.CenterId == centerId
                    && clinicIds.Contains(a.ClinicId))
                .ToListAsync(cancellationToken);

        // تقييم واحد لكل عيادة: الأحدث حسب AssessedAt عند التعدد غير المتوقع.
        var assessmentByClinic = assessments
            .GroupBy(a => a.ClinicId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AssessedAt).First());

        var deferredActions = await _clinicWorkspace.QueryDeferredActions(asNoTracking: true)
            .Where(d => d.MemberId == memberId
                && d.CenterId == centerId
                && d.Status != DeferredStatusClosed)
            .OrderByDescending(d => d.RequestedAt)
            .ToListAsync(cancellationToken);

        var openDeferredByAssessment = deferredActions
            .Where(d => d.ClinicAssessmentId.HasValue && d.Status == DeferredStatusRequested)
            .GroupBy(d => d.ClinicAssessmentId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var resultStatuses = await _referenceData.ResultStatuses(asNoTracking: true)
            .Select(s => new { s.StatusId, s.NameAr })
            .ToListAsync(cancellationToken);
        var resultStatusMap = resultStatuses.ToDictionary(s => s.StatusId, s => s.NameAr);

        var actionTypeIds = deferredActions.Select(d => d.ActionTypeId).Distinct().ToList();
        var actionTypes = actionTypeIds.Count == 0
            ? new Dictionary<short, string>()
            : await _referenceData.RequiredActionTypes(asNoTracking: true)
                .Where(t => actionTypeIds.Contains(t.ActionTypeId))
                .ToDictionaryAsync(t => t.ActionTypeId, t => t.NameAr, cancellationToken);

        var userIds = assessmentByClinic.Values.Select(a => a.AssessedByUserId)
            .Concat(deferredActions.Select(d => d.RequestedByUserId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingDecision = await _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
            .FirstOrDefaultAsync(d => d.MemberId == memberId && d.CenterId == centerId, cancellationToken);

        if (existingDecision is not null)
            userIds.Add(existingDecision.SupervisorUserId);

        var displayNames = userIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await _users.GetDisplayNamesByIdsAsync(userIds, cancellationToken);

        var clinicRows = new List<SupervisorClinicAssessmentRowDto>(mandatoryClinics.Count);
        foreach (var clinic in mandatoryClinics)
        {
            assessmentByClinic.TryGetValue(clinic.ClinicId, out var assessment);
            examByClinic.TryGetValue(clinic.ClinicId, out var exam);

            SupervisorClinicDeferredSummaryDto? deferredSummary = null;
            if (assessment is not null
                && openDeferredByAssessment.TryGetValue(assessment.AssessmentId, out var deferred))
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
                displayNames.TryGetValue(assessment.AssessedByUserId, out doctorName);

            string? resultName = null;
            if (assessment is not null)
                resultStatusMap.TryGetValue(assessment.ResultStatusId, out resultName);

            clinicRows.Add(new SupervisorClinicAssessmentRowDto
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

        var mandatoryPairs = mandatoryClinics
            .Select(c => (c.ClinicId, c.Code))
            .ToList();
        var assessedIds = assessmentByClinic.Keys.ToHashSet();
        var missingCodes = MandatoryClinicCompleteness.GetMissingClinicCodes(mandatoryPairs, assessedIds);
        var clinicsComplete = MandatoryClinicCompleteness.AreComplete(mandatoryPairs, assessedIds);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var clinicNameById = mandatoryClinics.ToDictionary(c => c.ClinicId, c => c.NameAr);
        var deferredItems = deferredActions.Select(d =>
        {
            actionTypes.TryGetValue(d.ActionTypeId, out var actionName);
            displayNames.TryGetValue(d.RequestedByUserId, out var requestedByName);
            string? clinicName = null;
            if (d.ClinicId.HasValue)
                clinicNameById.TryGetValue(d.ClinicId.Value, out clinicName);

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

        var hasOpenDeferred = deferredItems.Any(d => d.Status == DeferredStatusRequested);
        var hasPendingInvestigations = medicalRequests.PendingCount > 0
            && medicalRequests.Items.Any(i =>
                i.IsPending
                && string.Equals(
                    i.RequestKind,
                    MemberMedicalRequestService.RequestKindInvestigation,
                    StringComparison.OrdinalIgnoreCase));

        var warnings = new List<string>();
        if (hasPendingInvestigations)
            warnings.Add(PendingInvestigationWarningAr);
        if (hasOpenDeferred)
            warnings.Add(OpenDeferredWarningAr);

        string? blockReason = null;
        var canIssue = false;
        if (existingDecision is not null)
        {
            blockReason = ExistingDecisionBlockReasonAr;
        }
        else if (mandatoryClinics.Count == 0)
        {
            blockReason = NoMandatoryClinicsBlockReasonAr;
        }
        else if (!clinicsComplete)
        {
            blockReason = IncompleteClinicsBlockReasonAr;
        }
        else
        {
            canIssue = true;
        }

        SupervisorFinalDecisionSummaryDto? decisionSummary = null;
        if (existingDecision is not null)
        {
            resultStatusMap.TryGetValue(existingDecision.ResultStatusId, out var decisionResultName);
            displayNames.TryGetValue(existingDecision.SupervisorUserId, out var supervisorName);
            decisionSummary = new SupervisorFinalDecisionSummaryDto
            {
                FinalDecisionId = existingDecision.FinalDecisionId,
                ResultStatusId = existingDecision.ResultStatusId,
                ResultStatusNameAr = decisionResultName,
                RejectionReason = existingDecision.RejectionReason,
                DisagreementJustification = existingDecision.DisagreementJustification,
                SupervisorUserId = existingDecision.SupervisorUserId,
                SupervisorUserName = supervisorName,
                DecidedAt = existingDecision.DecidedAt,
                UpdatedAt = existingDecision.UpdatedAt
            };
        }

        var userId = RequireUserId();
        RecordViewAudit(userId, centerId, memberId, clinicsComplete, canIssue);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SupervisorAssessmentFileDto
        {
            MemberId = memberId,
            CenterId = centerId,
            Member = memberDto,
            HasGeneralNotesAlert = memberDto.HasGeneralNotesAlert,
            LatestGeneralNote = memberDto.GeneralNotes.Count > 0 ? memberDto.GeneralNotes[0] : null,
            ClinicAssessments = clinicRows,
            AreMandatoryClinicsComplete = clinicsComplete,
            CanIssueFinalDecision = canIssue,
            MissingClinicCodes = missingCodes,
            BlockReasonAr = blockReason,
            WarningsAr = warnings,
            HasPendingInvestigations = hasPendingInvestigations,
            HasOpenDeferredActions = hasOpenDeferred,
            DeferredActions = deferredItems,
            MedicalRequests = medicalRequests,
            ExistingFinalDecision = decisionSummary
        };
    }

    #region Private Helpers

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

        if (_fieldPermissions is not null)
            await _fieldPermissions.ApplyReadToDetailsAsync(dto, cancellationToken);

        return dto;
    }

    private void RecordViewAudit(
        string userId,
        Guid centerId,
        Guid memberId,
        bool clinicsComplete,
        bool canIssue)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = AssessmentFileViewEventType,
            Result = "SUCCESS",
            EntityType = nameof(Member),
            EntityId = memberId.ToString(),
            ReasonText = "اطلاع المشرف على ملف التقييم الطبي المجمّع",
            OldValues = null,
            NewValues = JsonSerializer.Serialize(new
            {
                AreMandatoryClinicsComplete = clinicsComplete,
                CanIssueFinalDecision = canIssue
            }),
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
            throw new UnauthenticatedUserException();
        return _currentUser.UserId;
    }

    private sealed record ExamSnapshot(Guid ClinicId, Guid ExamId);

    #endregion
}
