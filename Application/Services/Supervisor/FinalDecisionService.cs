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
using Domain.Entities.Archive;
using Domain.Entities.Audit;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Supervisor;

/// <summary>
/// إصدار وتعديل القرار الطبي النهائي المعتمد مع مبرر الاختلاف،
/// ربط DeferredAction، وسجل تاريخ التغيّر، وإنشاء/تحديث الأرشيف (FR-053…FR-056، FR-069).
/// </summary>
public class FinalDecisionService : IFinalDecisionService
{
    public const string FinalDecisionIssuedEventType = "FinalDecisionIssued";
    public const string FinalDecisionAmendedEventType = "FinalDecisionAmended";
    public const string MemberArchiveCreatedEventType = "MemberArchiveCreated";
    public const string MemberArchiveUpdatedEventType = "MemberArchiveUpdated";
    public const string TargetScopeOneClinic = "ONE_CLINIC";
    public const string ObjectionStatusOpen = "OPEN";
    public const string DeferredStatusRequested = "REQUESTED";
    public const string DeferredStatusClosed = "CLOSED";
    public const string ExportStatusUnexported = "UNEXPORTED";
    public const string ExportStatusExported = "EXPORTED";

    public const short AcceptedResultStatusId = ClinicAssessmentService.AcceptedResultStatusId;
    public const short RejectedResultStatusId = ClinicAssessmentService.RejectedResultStatusId;
    public const short DeferredResultStatusId = ClinicAssessmentService.DeferredResultStatusId;

    /// <summary>
    /// يحدد أهلية الملف لقائمة التصدير للتجنيد: قبول/رفض نهائي غير مصدَّر فقط.
    /// القرار المؤجل لا يُدرج قبل حسمه (FR-055 A5 / FR-056 / BR-018).
    /// </summary>
    public static bool IsEligibleForRecruitmentExport(short? resultStatusId, string? exportStatus)
    {
        if (resultStatusId is not AcceptedResultStatusId and not RejectedResultStatusId)
            return false;

        return string.Equals(
            exportStatus,
            ExportStatusUnexported,
            StringComparison.OrdinalIgnoreCase);
    }

    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;
    private readonly IMemberRepository _members;
    private readonly IClinicWorkspaceRepository _clinicWorkspace;
    private readonly ICenterClinicRepository _centerClinics;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IMedicalCenterRepository _centers;
    private readonly IMemberArchiveRepository _archives;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;

    public FinalDecisionService(
        IPermissionGate permissionGate,
        ICurrentUserService currentUser,
        IMemberRepository members,
        IClinicWorkspaceRepository clinicWorkspace,
        ICenterClinicRepository centerClinics,
        IReferenceDataRepository referenceData,
        IMedicalCenterRepository centers,
        IMemberArchiveRepository archives,
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
            actionType = await ResolveAndValidateActionTypeAsync(request, cancellationToken);

        var member = await _members.GetByIdInCenterAsync(
            memberId,
            centerId,
            asNoTracking: false,
            cancellationToken);
        if (member is null)
            throw new MemberNotFoundException();

        EnsureExpectedRevision(member, request.ExpectedDataRevision);

        var existing = await _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
            .AnyAsync(d => d.MemberId == memberId, cancellationToken);
        if (existing)
            throw new FinalDecisionAlreadyExistsException();

        var (disagreeing, clinicCodeById) = await LoadDisagreeingAssessmentsAsync(
            memberId,
            centerId,
            request.ResultStatusId,
            cancellationToken);

        var justification = ResolveJustification(request.DisagreementJustification, disagreeing, clinicCodeById);
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
            deferredAction = CreateFinalDeferredAction(decision, request, actionType!, userId, now);
            _clinicWorkspace.AddDeferredAction(deferredAction);
        }

        var objections = CreateObjections(
            disagreeing,
            memberId,
            centerId,
            decisionId,
            userId,
            justification,
            now);

        BumpMemberRevision(member, userId, now);

        var disagreeingCodes = disagreeing.Select(a => clinicCodeById[a.ClinicId]).ToList();
        // FR-069 / BR-017: الأرشيف يُنشأ في نفس وحدة العمل قبل COMMIT لإرضاء ctFinalDecisionArchive.
        var archive = await CreateArchiveForDecisionAsync(decision, now, cancellationToken);

        RecordIssuedAudit(
            userId,
            centerId,
            memberId,
            decision,
            disagreeingCodes,
            objections.Count,
            deferredAction?.DeferredActionId,
            archive);

        RecordArchiveCreatedAudit(userId, centerId, memberId, archive);

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

        return await MapDecisionDtoAsync(
            decision,
            member,
            disagreeingCodes,
            objections.Count,
            deferredAction,
            actionType,
            archive,
            archiveTouched: true,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AmendFinalDecisionDto> AmendAsync(
        Guid memberId,
        AmendFinalDecisionRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (memberId == Guid.Empty)
            throw new InvalidFinalDecisionDataException("معرّف المنتسب مطلوب.");

        if (request is null)
            throw new InvalidFinalDecisionDataException("بيانات تعديل القرار النهائي مطلوبة.");

        if (string.IsNullOrWhiteSpace(request.ChangeReason))
            throw new InvalidFinalDecisionDataException("سبب التعديل إلزامي عند تعديل القرار النهائي.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Clinical.FinalDecision,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        var isDeferred = request.ResultStatusId == DeferredResultStatusId;
        ValidateResultPayload(request, isDeferred);

        RequiredActionType? actionType = null;
        if (isDeferred)
            actionType = await ResolveAndValidateActionTypeAsync(request, cancellationToken);

        var member = await _members.GetByIdInCenterAsync(
            memberId,
            centerId,
            asNoTracking: false,
            cancellationToken);
        if (member is null)
            throw new MemberNotFoundException();

        EnsureExpectedRevision(member, request.ExpectedDataRevision);

        var decision = await _clinicWorkspace.QueryFinalDecisions(asNoTracking: false)
            .FirstOrDefaultAsync(
                d => d.MemberId == memberId && d.CenterId == centerId,
                cancellationToken);
        if (decision is null)
            throw new FinalDecisionNotFoundException();

        if (decision.ResultStatusId == request.ResultStatusId)
            throw new FinalDecisionUnchangedException();

        var (disagreeing, clinicCodeById) = await LoadDisagreeingAssessmentsAsync(
            memberId,
            centerId,
            request.ResultStatusId,
            cancellationToken);

        var justification = ResolveJustification(request.DisagreementJustification, disagreeing, clinicCodeById);
        var rejectionReason = request.ResultStatusId == RejectedResultStatusId
            ? request.RejectionReason!.Trim()
            : null;

        var changeReason = request.ChangeReason.Trim();
        var now = DateTimeOffset.UtcNow;
        var oldStatusId = decision.ResultStatusId;
        var previousExportStatus = member.ExportStatus;
        var previousLastExportedAt = member.LastExportedAt;
        var previousRejection = decision.RejectionReason;
        var previousJustification = decision.DisagreementJustification;

        // إغلاق الإجراءات المؤجلة المفتوحة المرتبطة بالقرار دون حذفها (قيود التفرد + FR-056).
        var openFinalDeferred = await _clinicWorkspace.QueryDeferredActions(asNoTracking: false)
            .Where(d => d.FinalDecisionId == decision.FinalDecisionId
                && d.Status != DeferredStatusClosed)
            .ToListAsync(cancellationToken);

        foreach (var open in openFinalDeferred)
            open.Status = DeferredStatusClosed;

        DeferredAction? newDeferredAction = null;
        if (isDeferred)
        {
            newDeferredAction = CreateFinalDeferredAction(decision, request, actionType!, userId, now);
            _clinicWorkspace.AddDeferredAction(newDeferredAction);
        }

        decision.ResultStatusId = request.ResultStatusId;
        decision.RejectionReason = rejectionReason;
        decision.DisagreementJustification = justification;
        decision.UpdatedByUserId = userId;
        decision.UpdatedAt = now;

        var objections = CreateObjections(
            disagreeing,
            memberId,
            centerId,
            decision.FinalDecisionId,
            userId,
            justification,
            now);

        // BR-021 / FR-056: إعادة حالة التصدير دائماً عند التعديل ومسح تاريخ التصدير السابق.
        // القرار المؤجل يبقى غير مصدَّر لكنه غير مؤهل لقائمة التصدير حتى الحسم.
        member.ExportStatus = ExportStatusUnexported;
        member.LastExportedAt = null;
        BumpMemberRevision(member, userId, now);

        var history = new FinalDecisionHistory
        {
            HistoryId = Guid.NewGuid(),
            FinalDecisionId = decision.FinalDecisionId,
            MemberId = memberId,
            CenterId = centerId,
            OldStatusId = oldStatusId,
            NewStatusId = request.ResultStatusId,
            ChangeReason = changeReason,
            ChangedByUserId = userId,
            ChangedAt = now,
            ExportResetPerformed = true
        };
        _clinicWorkspace.AddFinalDecisionHistory(history);

        var disagreeingCodes = disagreeing.Select(a => clinicCodeById[a.ClinicId]).ToList();
        // FR-069 / A3 وخطوة FR-056-9: تحديث سجل الأرشيف المرتبط دون فقد الربط أو التاريخ.
        var archive = await TouchArchiveForDecisionAsync(decision, now, cancellationToken);

        RecordAmendedAudit(
            userId,
            centerId,
            memberId,
            decision,
            history,
            oldStatusId,
            previousRejection,
            previousJustification,
            previousExportStatus,
            previousLastExportedAt,
            disagreeingCodes,
            objections.Count,
            newDeferredAction?.DeferredActionId,
            closedDeferredCount: openFinalDeferred.Count,
            archive);

        if (archive is not null)
            RecordArchiveUpdatedAudit(userId, centerId, memberId, archive);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var baseDto = await MapDecisionDtoAsync(
            decision,
            member,
            disagreeingCodes,
            objections.Count,
            newDeferredAction,
            actionType,
            archive,
            archiveTouched: archive is not null,
            cancellationToken);

        return new AmendFinalDecisionDto
        {
            FinalDecisionId = baseDto.FinalDecisionId,
            MemberId = baseDto.MemberId,
            CenterId = baseDto.CenterId,
            ResultStatusId = baseDto.ResultStatusId,
            ResultStatusNameAr = baseDto.ResultStatusNameAr,
            RejectionReason = baseDto.RejectionReason,
            DisagreementJustification = baseDto.DisagreementJustification,
            SupervisorUserId = baseDto.SupervisorUserId,
            DecidedAt = baseDto.DecidedAt,
            UpdatedAt = baseDto.UpdatedAt,
            MemberDataRevision = baseDto.MemberDataRevision,
            DisagreeingClinicCodes = baseDto.DisagreeingClinicCodes,
            ObjectionCount = baseDto.ObjectionCount,
            DeferredActionId = baseDto.DeferredActionId,
            DeferredAction = baseDto.DeferredAction,
            ArchiveCreated = baseDto.ArchiveCreated,
            ArchiveId = baseDto.ArchiveId,
            ArchiveNumber = baseDto.ArchiveNumber,
            ArchivedAt = baseDto.ArchivedAt,
            HistoryId = history.HistoryId,
            OldResultStatusId = oldStatusId,
            ChangeReason = changeReason,
            ExportResetPerformed = true,
            PreviousExportStatus = previousExportStatus
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FinalDecisionHistoryItemDto>> GetHistoryAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (memberId == Guid.Empty)
            throw new InvalidFinalDecisionDataException("معرّف المنتسب مطلوب.");

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

        var rows = await _clinicWorkspace.QueryFinalDecisionHistories(asNoTracking: true)
            .Where(h => h.MemberId == memberId && h.CenterId == centerId)
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.HistoryId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return Array.Empty<FinalDecisionHistoryItemDto>();

        var statusIds = rows
            .SelectMany(h => new[] { h.OldStatusId, h.NewStatusId })
            .Distinct()
            .ToList();

        var statusNames = await _referenceData.ResultStatuses(asNoTracking: true)
            .Where(s => statusIds.Contains(s.StatusId))
            .ToDictionaryAsync(s => s.StatusId, s => s.NameAr, cancellationToken);

        return rows.Select(h => new FinalDecisionHistoryItemDto
        {
            HistoryId = h.HistoryId,
            FinalDecisionId = h.FinalDecisionId,
            MemberId = h.MemberId,
            CenterId = h.CenterId,
            OldStatusId = h.OldStatusId,
            OldStatusNameAr = statusNames.GetValueOrDefault(h.OldStatusId),
            NewStatusId = h.NewStatusId,
            NewStatusNameAr = statusNames.GetValueOrDefault(h.NewStatusId),
            ChangeReason = h.ChangeReason,
            ChangedByUserId = h.ChangedByUserId,
            ChangedAt = h.ChangedAt,
            ExportResetPerformed = h.ExportResetPerformed
        }).ToList();
    }

    #region Private Helpers

    private static void EnsureExpectedRevision(Member member, long? expectedDataRevision)
    {
        if (expectedDataRevision.HasValue && expectedDataRevision.Value != member.DataRevision)
            throw new FinalDecisionConcurrencyException();
    }

    private static void BumpMemberRevision(Member member, string userId, DateTimeOffset now)
    {
        member.DataRevision += 1;
        member.UpdatedByUserId = userId;
        member.UpdatedAt = now;
    }

    private async Task<(List<ClinicAssessment> Disagreeing, Dictionary<Guid, string> ClinicCodeById)>
        LoadDisagreeingAssessmentsAsync(
            Guid memberId,
            Guid centerId,
            short resultStatusId,
            CancellationToken cancellationToken)
    {
        var mandatoryClinics = await SupervisorMandatoryClinicLoader.LoadAsync(
            centerId,
            _centerClinics,
            _referenceData,
            cancellationToken);

        if (mandatoryClinics.Count == 0)
            throw new IncompleteClinicAssessmentsException(Array.Empty<string>());

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
            .Where(a => a.ResultStatusId != resultStatusId)
            .OrderBy(a => clinicCodeById[a.ClinicId])
            .ToList();

        return (disagreeing, clinicCodeById);
    }

    private static string? ResolveJustification(
        string? disagreementJustification,
        IReadOnlyList<ClinicAssessment> disagreeing,
        IReadOnlyDictionary<Guid, string> clinicCodeById)
    {
        var justification = TrimOrNull(disagreementJustification);
        if (disagreeing.Count > 0 && justification is null)
        {
            throw new FinalDecisionDisagreementJustificationRequiredException(
                disagreeing.Select(a => clinicCodeById[a.ClinicId]).ToList());
        }

        return disagreeing.Count == 0 ? null : justification;
    }

    private List<SupervisorObjection> CreateObjections(
        IReadOnlyList<ClinicAssessment> disagreeing,
        Guid memberId,
        Guid centerId,
        Guid decisionId,
        string userId,
        string? justification,
        DateTimeOffset now)
    {
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

        return objections;
    }

    private async Task<FinalDecisionDto> MapDecisionDtoAsync(
        FinalDecision decision,
        Member member,
        IReadOnlyList<string> disagreeingCodes,
        int objectionCount,
        DeferredAction? deferredAction,
        RequiredActionType? actionType,
        MemberArchive? archive,
        bool archiveTouched,
        CancellationToken cancellationToken)
    {
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
            ObjectionCount = objectionCount,
            DeferredActionId = deferredAction?.DeferredActionId,
            DeferredAction = deferredDto,
            ArchiveCreated = archiveTouched,
            ArchiveId = archive?.ArchiveId,
            ArchiveNumber = archive?.ArchiveNumber,
            ArchivedAt = archive?.ArchivedAt
        };
    }

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
    /// ينشئ سجل أرشيف مرتبطاً بالقرار والمركز برقم فريد يحمل رمز المركز (FR-069 / BR-017 / BR-053).
    /// يُدرج ضمن نفس وحدة العمل قبل COMMIT لأن قيد ctFinalDecisionArchive يتطلب سطراً مطابقاً واحداً.
    /// </summary>
    private async Task<MemberArchive> CreateArchiveForDecisionAsync(
        FinalDecision decision,
        DateTimeOffset archivedAt,
        CancellationToken cancellationToken)
    {
        var center = await _centers.GetByIdAsync(decision.CenterId, asNoTracking: true, cancellationToken);
        if (center is null || string.IsNullOrWhiteSpace(center.Code))
        {
            throw new InvalidFinalDecisionDataException(
                "تعذر إنشاء سجل الأرشيف: رمز مركز صدور القرار غير متوفر.");
        }

        var archiveNumber = await _archives.AllocateArchiveNumberAsync(
            decision.CenterId,
            center.Code,
            cancellationToken);

        var archive = new MemberArchive
        {
            ArchiveId = Guid.NewGuid(),
            MemberId = decision.MemberId,
            FinalDecisionId = decision.FinalDecisionId,
            CenterId = decision.CenterId,
            ArchiveNumber = archiveNumber,
            ArchivedAt = archivedAt,
            DigitalCopyFileId = null,
            UpdatedAt = archivedAt
        };

        _archives.Add(archive);
        return archive;
    }

    /// <summary>
    /// يحدّث بصمة سجل الأرشيف عند تعديل القرار (FR-069 / A3). النتيجة تُقرأ من القرار النهائي لا من صف الأرشيف.
    /// </summary>
    private async Task<MemberArchive?> TouchArchiveForDecisionAsync(
        FinalDecision decision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        var archive = await _archives.GetByFinalDecisionIdAsync(
            decision.FinalDecisionId,
            asNoTracking: false,
            cancellationToken);

        if (archive is null)
            return null;

        archive.UpdatedAt = updatedAt;
        return archive;
    }

    private void RecordIssuedAudit(
        string userId,
        Guid centerId,
        Guid memberId,
        FinalDecision decision,
        IReadOnlyList<string> disagreeingClinicCodes,
        int objectionCount,
        Guid? deferredActionId,
        MemberArchive archive)
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
                ArchiveCreated = true,
                archive.ArchiveId,
                archive.ArchiveNumber,
                archive.ArchivedAt
            }),
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });
    }

    private void RecordArchiveCreatedAudit(
        string userId,
        Guid centerId,
        Guid memberId,
        MemberArchive archive)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = MemberArchiveCreatedEventType,
            Result = "SUCCESS",
            EntityType = nameof(MemberArchive),
            EntityId = archive.ArchiveId.ToString(),
            ReasonText = $"إنشاء سجل أرشيف للمنتسب {memberId:D} برقم {archive.ArchiveNumber}",
            OldValues = null,
            NewValues = JsonSerializer.Serialize(new
            {
                archive.ArchiveId,
                archive.MemberId,
                archive.FinalDecisionId,
                archive.CenterId,
                archive.ArchiveNumber,
                archive.ArchivedAt
            }),
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });
    }

    private void RecordArchiveUpdatedAudit(
        string userId,
        Guid centerId,
        Guid memberId,
        MemberArchive archive)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = MemberArchiveUpdatedEventType,
            Result = "SUCCESS",
            EntityType = nameof(MemberArchive),
            EntityId = archive.ArchiveId.ToString(),
            ReasonText = $"تحديث سجل الأرشيف للمنتسب {memberId:D} بعد تعديل القرار النهائي",
            OldValues = null,
            NewValues = JsonSerializer.Serialize(new
            {
                archive.ArchiveId,
                archive.MemberId,
                archive.FinalDecisionId,
                archive.CenterId,
                archive.ArchiveNumber,
                archive.ArchivedAt,
                archive.UpdatedAt
            }),
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });
    }

    private void RecordAmendedAudit(
        string userId,
        Guid centerId,
        Guid memberId,
        FinalDecision decision,
        FinalDecisionHistory history,
        short oldStatusId,
        string? previousRejection,
        string? previousJustification,
        string previousExportStatus,
        DateTimeOffset? previousLastExportedAt,
        IReadOnlyList<string> disagreeingClinicCodes,
        int objectionCount,
        Guid? deferredActionId,
        int closedDeferredCount,
        MemberArchive? archive)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = FinalDecisionAmendedEventType,
            Result = "SUCCESS",
            EntityType = nameof(FinalDecision),
            EntityId = decision.FinalDecisionId.ToString(),
            ReasonText = history.ChangeReason,
            OldValues = JsonSerializer.Serialize(new
            {
                ResultStatusId = oldStatusId,
                RejectionReason = previousRejection,
                DisagreementJustification = previousJustification,
                ExportStatus = previousExportStatus,
                LastExportedAt = previousLastExportedAt
            }),
            NewValues = JsonSerializer.Serialize(new
            {
                history.HistoryId,
                decision.FinalDecisionId,
                decision.MemberId,
                decision.CenterId,
                OldStatusId = oldStatusId,
                NewStatusId = decision.ResultStatusId,
                decision.RejectionReason,
                decision.DisagreementJustification,
                ChangeReason = history.ChangeReason,
                ExportResetPerformed = true,
                ExportStatus = ExportStatusUnexported,
                LastExportedAt = (DateTimeOffset?)null,
                DisagreeingClinicCodes = disagreeingClinicCodes,
                ObjectionCount = objectionCount,
                DeferredActionId = deferredActionId,
                ClosedDeferredCount = closedDeferredCount,
                ArchiveUpdated = archive is not null,
                ArchiveId = archive?.ArchiveId,
                ArchiveNumber = archive?.ArchiveNumber
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
