using System.Net;
using System.Text.Json;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.Supervisor;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.ClinicWorkspace;
using Domain.Entities.Audit;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Supervisor;

/// <summary>
/// إصدار القرار الطبي النهائي المعتمد (قبول/رفض/مؤجل) مع مبرر الاختلاف وربط DeferredAction (FR-053 / FR-054 / FR-055).
/// التعديل مسار لاحق (MH-P6-040).
/// </summary>
public class FinalDecisionService : IFinalDecisionService
{
    public const string FinalDecisionIssuedEventType = "FinalDecisionIssued";
    public const string TargetScopeOneClinic = "ONE_CLINIC";
    public const string ObjectionStatusOpen = "OPEN";
    public const string DeferredStatusRequested = "REQUESTED";

    public const short AcceptedResultStatusId = ClinicAssessmentService.AcceptedResultStatusId;
    public const short RejectedResultStatusId = ClinicAssessmentService.RejectedResultStatusId;
    public const short DeferredResultStatusId = ClinicAssessmentService.DeferredResultStatusId;

    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;
    private readonly IMemberRepository _members;
    private readonly IClinicWorkspaceRepository _clinicWorkspace;
    private readonly ICenterClinicRepository _centerClinics;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;

    public FinalDecisionService(
        IPermissionGate permissionGate,
        ICurrentUserService currentUser,
        IMemberRepository members,
        IClinicWorkspaceRepository clinicWorkspace,
        ICenterClinicRepository centerClinics,
        IReferenceDataRepository referenceData,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork)
    {
        _permissionGate = permissionGate;
        _currentUser = currentUser;
        _members = members;
        _clinicWorkspace = clinicWorkspace;
        _centerClinics = centerClinics;
        _referenceData = referenceData;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<FinalDecisionDto> IssueAsync(
        Guid memberId,
        IssueFinalDecisionRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (memberId == Guid.Empty)
            throw new InvalidFinalDecisionDataException("معرّف المنتسب مطلوب.");

        if (request is null)
            throw new InvalidFinalDecisionDataException("بيانات القرار النهائي مطلوبة.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Clinical.FinalDecision,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        var isDeferred = request.ResultStatusId == DeferredResultStatusId;
        ValidateResultPayload(request, isDeferred);

        RequiredActionType? actionType = null;
        if (isDeferred)
        {
            actionType = await ResolveAndValidateActionTypeAsync(request, cancellationToken);
        }

        var member = await _members.GetByIdInCenterAsync(
            memberId,
            centerId,
            asNoTracking: false,
            cancellationToken);
        if (member is null)
            throw new MemberNotFoundException();

        if (request.ExpectedDataRevision.HasValue
            && request.ExpectedDataRevision.Value != member.DataRevision)
        {
            throw new FinalDecisionConcurrencyException();
        }

        var existing = await _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
            .AnyAsync(d => d.MemberId == memberId, cancellationToken);
        if (existing)
            throw new FinalDecisionAlreadyExistsException();

        var mandatoryClinics = await SupervisorMandatoryClinicLoader.LoadAsync(
            centerId,
            _centerClinics,
            _referenceData,
            cancellationToken);

        if (mandatoryClinics.Count == 0)
        {
            throw new IncompleteClinicAssessmentsException(Array.Empty<string>());
        }

        var clinicIds = mandatoryClinics.Select(c => c.ClinicId).ToList();
        var assessments = await _clinicWorkspace.QueryClinicAssessments(asNoTracking: true)
            .Where(a => a.MemberId == memberId
                && a.CenterId == centerId
                && clinicIds.Contains(a.ClinicId))
            .ToListAsync(cancellationToken);

        var assessmentByClinic = assessments
            .GroupBy(a => a.ClinicId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AssessedAt).First());

        var mandatoryPairs = mandatoryClinics.Select(c => (c.ClinicId, c.Code)).ToList();
        var assessedIds = assessmentByClinic.Keys.ToHashSet();
        var missingCodes = MandatoryClinicCompleteness.GetMissingClinicCodes(mandatoryPairs, assessedIds);
        if (missingCodes.Count > 0)
            throw new IncompleteClinicAssessmentsException(missingCodes);

        var clinicCodeById = mandatoryClinics.ToDictionary(c => c.ClinicId, c => c.Code);
        var disagreeing = assessmentByClinic.Values
            .Where(a => a.ResultStatusId != request.ResultStatusId)
            .OrderBy(a => clinicCodeById[a.ClinicId])
            .ToList();

        var justification = TrimOrNull(request.DisagreementJustification);
        if (disagreeing.Count > 0 && justification is null)
        {
            throw new FinalDecisionDisagreementJustificationRequiredException(
                disagreeing.Select(a => clinicCodeById[a.ClinicId]).ToList());
        }

        if (disagreeing.Count == 0)
            justification = null;

        var rejectionReason = request.ResultStatusId == RejectedResultStatusId
            ? request.RejectionReason!.Trim()
            : null;

        var now = DateTimeOffset.UtcNow;
        var decisionId = Guid.NewGuid();
        var decision = new FinalDecision
        {
            FinalDecisionId = decisionId,
            MemberId = memberId,
            CenterId = centerId,
            SupervisorUserId = userId,
            ResultStatusId = request.ResultStatusId,
            RejectionReason = rejectionReason,
            DisagreementJustification = justification,
            DecidedAt = now,
            UpdatedByUserId = userId,
            UpdatedAt = now
        };

        _clinicWorkspace.AddFinalDecision(decision);

        DeferredAction? deferredAction = null;
        if (isDeferred)
        {
            deferredAction = CreateFinalDeferredAction(
                decision,
                request,
                actionType!,
                userId,
                now);
            _clinicWorkspace.AddDeferredAction(deferredAction);
        }

        var objections = new List<SupervisorObjection>(disagreeing.Count);
        foreach (var assessment in disagreeing)
        {
            var objection = new SupervisorObjection
            {
                ObjectionId = Guid.NewGuid(),
                MemberId = memberId,
                CenterId = centerId,
                SupervisorUserId = userId,
                TargetScope = TargetScopeOneClinic,
                ClinicAssessmentId = assessment.AssessmentId,
                ObjectionReason = justification!,
                FinalDecisionId = decisionId,
                Status = ObjectionStatusOpen,
                CreatedAt = now,
                ResolvedAt = null,
                ResolutionNote = null
            };
            _clinicWorkspace.AddSupervisorObjection(objection);
            objections.Add(objection);
        }

        member.DataRevision += 1;
        member.UpdatedByUserId = userId;
        member.UpdatedAt = now;

        var disagreeingCodes = disagreeing.Select(a => clinicCodeById[a.ClinicId]).ToList();
        RecordIssuedAudit(
            userId,
            centerId,
            memberId,
            decision,
            disagreeingCodes,
            objections.Count,
            deferredAction?.DeferredActionId);

        var archiveCreated = TryCreateArchiveHook(decision);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
                    .AnyAsync(d => d.MemberId == memberId, cancellationToken))
            {
                throw new FinalDecisionAlreadyExistsException();
            }

            throw;
        }

        var statusName = await _referenceData.ResultStatuses(asNoTracking: true)
            .Where(s => s.StatusId == decision.ResultStatusId)
            .Select(s => s.NameAr)
            .FirstOrDefaultAsync(cancellationToken);

        FinalDecisionDeferredActionDto? deferredDto = null;
        if (deferredAction is not null)
        {
            deferredDto = new FinalDecisionDeferredActionDto
            {
                DeferredActionId = deferredAction.DeferredActionId,
                DeferReason = deferredAction.DeferReason,
                DeferUntil = deferredAction.DeferUntil!.Value,
                ActionTypeId = deferredAction.ActionTypeId,
                ActionTypeNameAr = actionType!.NameAr,
                ActionDescription = deferredAction.ActionDescription,
                FollowupNote = deferredAction.FollowupNote,
                Status = deferredAction.Status
            };
        }

        return new FinalDecisionDto
        {
            FinalDecisionId = decision.FinalDecisionId,
            MemberId = decision.MemberId,
            CenterId = decision.CenterId,
            ResultStatusId = decision.ResultStatusId,
            ResultStatusNameAr = statusName,
            RejectionReason = decision.RejectionReason,
            DisagreementJustification = decision.DisagreementJustification,
            SupervisorUserId = decision.SupervisorUserId,
            DecidedAt = decision.DecidedAt,
            UpdatedAt = decision.UpdatedAt,
            MemberDataRevision = member.DataRevision,
            DisagreeingClinicCodes = disagreeingCodes,
            ObjectionCount = objections.Count,
            DeferredActionId = deferredAction?.DeferredActionId,
            DeferredAction = deferredDto,
            ArchiveCreated = archiveCreated
        };
    }

    #region Private Helpers

    private static void ValidateResultPayload(IssueFinalDecisionRequest request, bool isDeferred)
    {
        if (request.ResultStatusId is not AcceptedResultStatusId
            and not RejectedResultStatusId
            and not DeferredResultStatusId)
        {
            throw new InvalidFinalDecisionDataException(
                "نتيجة القرار النهائي يجب أن تكون مقبول (1) أو مرفوض (2) أو مؤجل (3).");
        }

        if (request.ResultStatusId == RejectedResultStatusId)
        {
            if (string.IsNullOrWhiteSpace(request.RejectionReason))
            {
                throw new InvalidFinalDecisionDataException(
                    "سبب الرفض مطلوب عند اختيار نتيجة مرفوض.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            throw new InvalidFinalDecisionDataException(
                "سبب الرفض يُسمح به فقط عند نتيجة مرفوض.");
        }

        if (!isDeferred)
        {
            if (request.ActionTypeId.HasValue
                || request.DeferUntil.HasValue
                || request.TargetClinicId.HasValue
                || !string.IsNullOrWhiteSpace(request.DeferReason)
                || !string.IsNullOrWhiteSpace(request.FollowupNote)
                || !string.IsNullOrWhiteSpace(request.ActionDescription))
            {
                throw new InvalidFinalDecisionDataException(
                    "حقول التأجيل تُسمح بها فقط عند اختيار مؤجل.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(request.DeferReason))
            throw new InvalidFinalDecisionDataException("سبب التأجيل إلزامي عند اختيار مؤجل.");

        if (!request.DeferUntil.HasValue)
            throw new InvalidFinalDecisionDataException("مدة/تاريخ انتهاء التأجيل إلزامي للقرار النهائي المؤجل.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.DeferUntil.Value < today)
            throw new InvalidFinalDecisionDataException("تاريخ انتهاء التأجيل لا يمكن أن يكون في الماضي.");

        if (!request.ActionTypeId.HasValue || request.ActionTypeId.Value <= 0)
            throw new InvalidFinalDecisionDataException("نوع الإجراء المطلوب إلزامي عند اختيار مؤجل.");

        if (string.IsNullOrWhiteSpace(request.FollowupNote))
            throw new InvalidFinalDecisionDataException("الملاحظة التوضيحية إلزامية عند اختيار مؤجل.");
    }

    private async Task<RequiredActionType> ResolveAndValidateActionTypeAsync(
        IssueFinalDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var actionType = await _referenceData.RequiredActionTypes()
            .FirstOrDefaultAsync(
                a => a.ActionTypeId == request.ActionTypeId!.Value && a.IsActive,
                cancellationToken);

        if (actionType is null)
            throw new InvalidFinalDecisionDataException("نوع الإجراء المطلوب غير موجود أو غير نشط.");

        if (actionType.RequiresDescription)
        {
            if (string.IsNullOrWhiteSpace(request.ActionDescription))
                throw new InvalidFinalDecisionDataException("توصيف الإجراء إلزامي لهذا النوع.");
        }
        else if (!string.IsNullOrWhiteSpace(request.ActionDescription))
        {
            throw new InvalidFinalDecisionDataException("هذا النوع من الإجراء لا يقبل توصيفاً نصياً.");
        }

        if (actionType.RequiresTargetClinic)
        {
            if (!request.TargetClinicId.HasValue || request.TargetClinicId.Value == Guid.Empty)
                throw new InvalidFinalDecisionDataException("العيادة المستهدفة إلزامية لهذا النوع من الإجراء.");
        }
        else if (request.TargetClinicId.HasValue)
        {
            throw new InvalidFinalDecisionDataException("هذا النوع من الإجراء لا يقبل عيادة مستهدفة.");
        }

        return actionType;
    }

    private static DeferredAction CreateFinalDeferredAction(
        FinalDecision decision,
        IssueFinalDecisionRequest request,
        RequiredActionType actionType,
        string userId,
        DateTimeOffset now)
    {
        // مسار القرار النهائي: ClinicId و ClinicAssessmentId فارغان (XOR مع FinalDecisionId).
        return new DeferredAction
        {
            DeferredActionId = Guid.NewGuid(),
            MemberId = decision.MemberId,
            CenterId = decision.CenterId,
            ClinicId = null,
            ClinicAssessmentId = null,
            FinalDecisionId = decision.FinalDecisionId,
            DeferReason = request.DeferReason!.Trim(),
            DeferUntil = request.DeferUntil,
            ActionTypeId = actionType.ActionTypeId,
            TargetClinicId = actionType.RequiresTargetClinic ? request.TargetClinicId : null,
            ActionDescription = actionType.RequiresDescription
                ? request.ActionDescription!.Trim()
                : null,
            FollowupNote = request.FollowupNote!.Trim(),
            Status = DeferredStatusRequested,
            RequestedByUserId = userId,
            RequestedAt = now
        };
    }

    /// <summary>
    /// خطاف أرشفة القرار النهائي (BR-017). لا يُنشئ أرشيفاً هنا — يُستكمل في T-10.
    /// </summary>
    private static bool TryCreateArchiveHook(FinalDecision decision)
    {
        _ = decision;
        return false;
    }

    private void RecordIssuedAudit(
        string userId,
        Guid centerId,
        Guid memberId,
        FinalDecision decision,
        IReadOnlyList<string> disagreeingClinicCodes,
        int objectionCount,
        Guid? deferredActionId)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = FinalDecisionIssuedEventType,
            Result = "SUCCESS",
            EntityType = nameof(FinalDecision),
            EntityId = decision.FinalDecisionId.ToString(),
            ReasonText = $"إصدار قرار نهائي للمنتسب {memberId:D} بنتيجة {decision.ResultStatusId}",
            OldValues = null,
            NewValues = JsonSerializer.Serialize(new
            {
                decision.FinalDecisionId,
                decision.MemberId,
                decision.CenterId,
                decision.ResultStatusId,
                decision.RejectionReason,
                decision.DisagreementJustification,
                DisagreeingClinicCodes = disagreeingClinicCodes,
                ObjectionCount = objectionCount,
                DeferredActionId = deferredActionId,
                DeferredStatus = deferredActionId.HasValue ? DeferredStatusRequested : null,
                ArchiveHookInvoked = true,
                ArchiveCreated = false
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

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    #endregion
}
