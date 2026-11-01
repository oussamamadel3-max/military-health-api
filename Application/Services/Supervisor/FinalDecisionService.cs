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
using Domain.Entities.Patient;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Supervisor;

/// <summary>
/// إصدار القرار الطبي النهائي المعتمد (قبول/رفض) مع مبرر الاختلاف واعتراضات العيادات (FR-053 / FR-054).
/// التأجيل (3) والتعديل خارج هذه البطاقة.
/// </summary>
public class FinalDecisionService : IFinalDecisionService
{
    public const string FinalDecisionIssuedEventType = "FinalDecisionIssued";
    public const string TargetScopeOneClinic = "ONE_CLINIC";
    public const string ObjectionStatusOpen = "OPEN";

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
        ValidateResultPayload(request);

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

        // حماية تعارض بسيطة: رفع مراجعة المنتسب مع حفظ القرار في نفس الوحدة.
        member.DataRevision += 1;
        member.UpdatedByUserId = userId;
        member.UpdatedAt = now;

        RecordIssuedAudit(
            userId,
            centerId,
            memberId,
            decision,
            disagreeing.Select(a => clinicCodeById[a.ClinicId]).ToList(),
            objections.Count);

        // نقطة ربط صريحة لأرشفة القرار (BR-017 / T-10) — خارج نطاق MH-P6-020.
        var archiveCreated = TryCreateArchiveHook(decision);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // سباق جلستين على القيد الفريد MemberId.
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
            DisagreeingClinicCodes = disagreeing.Select(a => clinicCodeById[a.ClinicId]).ToList(),
            ObjectionCount = objections.Count,
            ArchiveCreated = archiveCreated
        };
    }

    #region Private Helpers

    private static void ValidateResultPayload(IssueFinalDecisionRequest request)
    {
        if (request.ResultStatusId == DeferredResultStatusId)
        {
            throw new InvalidFinalDecisionDataException(
                "تأجيل القرار النهائي يتم عبر مسار منفصل وليس عبر إصدار القبول/الرفض.");
        }

        if (request.ResultStatusId is not AcceptedResultStatusId and not RejectedResultStatusId)
        {
            throw new InvalidFinalDecisionDataException(
                "نتيجة القرار النهائي يجب أن تكون مقبول (1) أو مرفوض (2).");
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
        int objectionCount)
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
