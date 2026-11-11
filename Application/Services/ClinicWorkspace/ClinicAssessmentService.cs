using System.Net;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.ClinicWorkspace;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.ClinicWorkspace;

/// <summary>
/// محرك مشترك لحفظ تقييم العيادة والتأجيل وفرض قفل التحرير ومنع الفحص الأساسي المكرر
/// (FR-040 / FR-041 / شريحة كتابة FR-045).
/// </summary>
public class ClinicAssessmentService : IClinicAssessmentService
{
    public const string AssessmentSavedEventType = "ClinicAssessmentSaved";
    public const string ExamConflictEventType = "ClinicExamConflict";

    public const short AcceptedResultStatusId = 1;
    public const short RejectedResultStatusId = 2;
    public const short DeferredResultStatusId = 3;

    private readonly IClinicWorkspaceRepository _clinicWorkspace;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IUserRepository _users;
    private readonly ICenterClinicRepository _centerClinics;
    private readonly IMemberService _members;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;

    public ClinicAssessmentService(
        IClinicWorkspaceRepository clinicWorkspace,
        IReferenceDataRepository referenceData,
        IUserRepository users,
        ICenterClinicRepository centerClinics,
        IMemberService members,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        IPermissionGate permissionGate,
        ICurrentUserService currentUser)
    {
        _clinicWorkspace = clinicWorkspace;
        _referenceData = referenceData;
        _users = users;
        _centerClinics = centerClinics;
        _members = members;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _permissionGate = permissionGate;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<ClinicAssessmentSavedDto> SaveAsync(
        Guid memberId,
        SaveClinicAssessmentRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new InvalidClinicAssessmentDataException("بيانات التقييم مطلوبة.");

        if (memberId == Guid.Empty)
            throw new InvalidClinicAssessmentDataException("معرّف المنتسب مطلوب.");

        if (request.ClinicId == Guid.Empty)
            throw new InvalidClinicAssessmentDataException("معرّف العيادة مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Clinical.AssessmentWrite,
            clientCenterId,
            cancellationToken);

        await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Clinical.ExamWrite,
            clientCenterId,
            cancellationToken);

        var isDeferred = request.ResultStatusId == DeferredResultStatusId;
        if (isDeferred)
        {
            await _permissionGate.EnsureLocalAsync(
                MilitaryHealthPermissions.Clinical.DeferredAction,
                clientCenterId,
                cancellationToken);
        }

        var userId = RequireUserId();
        await EnsureDoctorClinicAsync(centerId, request.ClinicId, userId, cancellationToken);

        // عزل المركز: GetById يرفض منتسب مركز آخر.
        await _members.GetByIdAsync(memberId, clientCenterId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var editLock = await _clinicWorkspace.GetEditLockAsync(
            memberId,
            request.ClinicId,
            asNoTracking: false,
            cancellationToken);

        if (!IsOwnedActiveLock(editLock, userId, now))
        {
            await RecordConflictAuditAsync(
                userId,
                centerId,
                memberId,
                request.ClinicId,
                succeeded: false,
                reason: "رفض الحفظ بسبب غياب قفل التحرير أو ملكيته لطبيب آخر أو انتهاء المهلة.",
                cancellationToken);
            throw new ClinicExamEditLockConflictException();
        }

        ValidateAssessmentPayload(request);
        var actionType = await ResolveAndValidateActionTypeAsync(request, isDeferred, cancellationToken);

        await using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var exam = await _clinicWorkspace.QueryMedicalExams(asNoTracking: false)
                .FirstOrDefaultAsync(
                    e => e.MemberId == memberId
                        && e.ClinicId == request.ClinicId
                        && e.IsCurrent,
                    cancellationToken);

            if (exam is null)
            {
                exam = new MedicalExam
                {
                    ExamId = Guid.NewGuid(),
                    MemberId = memberId,
                    CenterId = centerId,
                    ClinicId = request.ClinicId,
                    ExaminerUserId = userId,
                    ExaminedAt = now,
                    ExamSequence = 1,
                    IsCurrent = true,
                    VersionNo = 1,
                    UpdatedByUserId = userId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _clinicWorkspace.AddMedicalExam(exam);
            }
            else
            {
                if (exam.CenterId != centerId)
                    throw new ClinicExamEditLockConflictException("الفحص الحالي خارج نطاق المركز النشط.");

                MedicalExamIdentity.RecordModification(exam, userId, now);
            }

            var assessment = await _clinicWorkspace.QueryClinicAssessments(asNoTracking: false)
                .FirstOrDefaultAsync(a => a.ExamId == exam.ExamId, cancellationToken);

            var previousStatus = assessment?.ResultStatusId;
            var rejectionReason = request.ResultStatusId == RejectedResultStatusId
                ? request.RejectionReason!.Trim()
                : null;
            var clinicalNotes = string.IsNullOrWhiteSpace(request.ClinicalNotes)
                ? null
                : request.ClinicalNotes.Trim();

            if (assessment is null)
            {
                assessment = new ClinicAssessment
                {
                    AssessmentId = Guid.NewGuid(),
                    ExamId = exam.ExamId,
                    MemberId = memberId,
                    CenterId = centerId,
                    ClinicId = request.ClinicId,
                    ResultStatusId = request.ResultStatusId,
                    RejectionReason = rejectionReason,
                    ClinicalNotes = clinicalNotes,
                    AssessedByUserId = userId,
                    AssessedAt = now,
                    UpdatedByUserId = userId,
                    UpdatedAt = now
                };
                _clinicWorkspace.AddClinicAssessment(assessment);
            }
            else
            {
                assessment.ResultStatusId = request.ResultStatusId;
                assessment.RejectionReason = rejectionReason;
                assessment.ClinicalNotes = clinicalNotes;
                assessment.UpdatedByUserId = userId;
                assessment.UpdatedAt = now;
            }

            Guid? deferredActionId = null;
            if (isDeferred)
            {
                deferredActionId = await UpsertDeferredActionAsync(
                    assessment,
                    request,
                    actionType!,
                    userId,
                    now,
                    cancellationToken);
            }
            else if (previousStatus == DeferredResultStatusId)
            {
                await CloseOpenDeferredActionsAsync(assessment.AssessmentId, now, cancellationToken);
            }

            _clinicWorkspace.RemoveEditLock(editLock!);

            RecordAssessmentSavedAudit(userId, centerId, memberId, request.ClinicId, assessment);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new ClinicAssessmentSavedDto
            {
                ExamId = exam.ExamId,
                AssessmentId = assessment.AssessmentId,
                DeferredActionId = deferredActionId,
                MemberId = memberId,
                CenterId = centerId,
                ClinicId = request.ClinicId,
                ResultStatusId = assessment.ResultStatusId,
                ExaminerUserId = exam.ExaminerUserId,
                AssessedByUserId = assessment.AssessedByUserId,
                ExaminedAt = exam.ExaminedAt,
                AssessedAt = assessment.AssessedAt,
                ExamVersionNo = exam.VersionNo
            };
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(cancellationToken);

            var duplicateExists = await _clinicWorkspace.QueryMedicalExams()
                .AnyAsync(
                    e => e.MemberId == memberId
                        && e.ClinicId == request.ClinicId
                        && e.IsCurrent,
                    cancellationToken);

            if (duplicateExists)
            {
                await RecordConflictAuditAsync(
                    userId,
                    centerId,
                    memberId,
                    request.ClinicId,
                    succeeded: false,
                    reason: "رفض حفظ فحص أساسي مكرر لنفس المنتسب والعيادة.",
                    cancellationToken);
                throw new DuplicateCurrentMedicalExamException();
            }

            throw;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ReleaseEditLockAsync(
        Guid memberId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (memberId == Guid.Empty)
            throw new InvalidClinicWorkspaceDataException("معرّف المنتسب مطلوب.");

        if (clinicId == Guid.Empty)
            throw new InvalidClinicWorkspaceDataException("معرّف العيادة مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Clinical.AssessmentWrite,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        await EnsureDoctorClinicAsync(centerId, clinicId, userId, cancellationToken);
        await _members.GetByIdAsync(memberId, clientCenterId, cancellationToken);

        var editLock = await _clinicWorkspace.GetEditLockAsync(
            memberId,
            clinicId,
            asNoTracking: false,
            cancellationToken);

        if (editLock is null)
            return;

        if (!string.Equals(editLock.LockedByUserId, userId, StringComparison.Ordinal))
            throw new ClinicExamEditLockConflictException("لا يمكن تحرير قفل يملكه طبيب آخر.");

        if (editLock.CenterId != centerId)
            throw new ClinicExamEditLockConflictException("القفل خارج نطاق المركز النشط.");

        _clinicWorkspace.RemoveEditLock(editLock);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    #region Validation

    private static void ValidateAssessmentPayload(SaveClinicAssessmentRequest request)
    {
        if (request.ResultStatusId is not (
                AcceptedResultStatusId or RejectedResultStatusId or DeferredResultStatusId))
        {
            throw new InvalidClinicAssessmentDataException(
                "قيمة التقييم يجب أن تكون مقبول أو مرفوض أو مؤجل.");
        }

        if (request.ResultStatusId == RejectedResultStatusId)
        {
            if (string.IsNullOrWhiteSpace(request.RejectionReason))
                throw new InvalidClinicAssessmentDataException("سبب الرفض إلزامي عند اختيار مرفوض.");
        }
        else if (!string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            throw new InvalidClinicAssessmentDataException(
                "سبب الرفض يُسمح به فقط عند اختيار مرفوض.");
        }

        if (request.ResultStatusId != DeferredResultStatusId)
        {
            if (request.ActionTypeId.HasValue
                || !string.IsNullOrWhiteSpace(request.DeferReason)
                || !string.IsNullOrWhiteSpace(request.FollowupNote)
                || !string.IsNullOrWhiteSpace(request.ActionDescription)
                || request.TargetClinicId.HasValue
                || request.DeferUntil.HasValue)
            {
                throw new InvalidClinicAssessmentDataException(
                    "حقول التأجيل تُسمح بها فقط عند اختيار مؤجل.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.DeferReason))
                throw new InvalidClinicAssessmentDataException("سبب التأجيل إلزامي عند اختيار مؤجل.");

            if (!request.ActionTypeId.HasValue || request.ActionTypeId.Value <= 0)
                throw new InvalidClinicAssessmentDataException("نوع الإجراء المطلوب إلزامي عند اختيار مؤجل.");

            if (string.IsNullOrWhiteSpace(request.FollowupNote))
                throw new InvalidClinicAssessmentDataException("ملاحظة المتابعة إلزامية عند اختيار مؤجل.");
        }
    }

    private async Task<RequiredActionType?> ResolveAndValidateActionTypeAsync(
        SaveClinicAssessmentRequest request,
        bool isDeferred,
        CancellationToken cancellationToken)
    {
        if (!isDeferred)
            return null;

        var actionType = await _referenceData.RequiredActionTypes()
            .FirstOrDefaultAsync(
                a => a.ActionTypeId == request.ActionTypeId!.Value && a.IsActive,
                cancellationToken);

        if (actionType is null)
            throw new InvalidClinicAssessmentDataException("نوع الإجراء المطلوب غير موجود أو غير نشط.");

        if (actionType.RequiresDescription)
        {
            if (string.IsNullOrWhiteSpace(request.ActionDescription))
                throw new InvalidClinicAssessmentDataException("توصيف الإجراء إلزامي لهذا النوع.");
        }
        else if (!string.IsNullOrWhiteSpace(request.ActionDescription))
        {
            throw new InvalidClinicAssessmentDataException("هذا النوع من الإجراء لا يقبل توصيفاً نصياً.");
        }

        if (actionType.RequiresTargetClinic)
        {
            if (!request.TargetClinicId.HasValue || request.TargetClinicId.Value == Guid.Empty)
                throw new InvalidClinicAssessmentDataException("العيادة المستهدفة إلزامية لهذا النوع من الإجراء.");
        }
        else if (request.TargetClinicId.HasValue)
        {
            throw new InvalidClinicAssessmentDataException("هذا النوع من الإجراء لا يقبل عيادة مستهدفة.");
        }

        return actionType;
    }

    #endregion

    #region Persistence helpers

    private async Task<Guid> UpsertDeferredActionAsync(
        ClinicAssessment assessment,
        SaveClinicAssessmentRequest request,
        RequiredActionType actionType,
        string userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var open = await _clinicWorkspace.QueryDeferredActions(asNoTracking: false)
            .Where(d => d.ClinicAssessmentId == assessment.AssessmentId && d.Status != "CLOSED")
            .OrderByDescending(d => d.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var deferReason = request.DeferReason!.Trim();
        var followupNote = request.FollowupNote!.Trim();
        var actionDescription = actionType.RequiresDescription
            ? request.ActionDescription!.Trim()
            : null;

        if (open is null)
        {
            var created = new DeferredAction
            {
                DeferredActionId = Guid.NewGuid(),
                MemberId = assessment.MemberId,
                CenterId = assessment.CenterId,
                ClinicId = assessment.ClinicId,
                ClinicAssessmentId = assessment.AssessmentId,
                FinalDecisionId = null,
                DeferReason = deferReason,
                DeferUntil = request.DeferUntil,
                ActionTypeId = actionType.ActionTypeId,
                TargetClinicId = actionType.RequiresTargetClinic ? request.TargetClinicId : null,
                ActionDescription = actionDescription,
                FollowupNote = followupNote,
                Status = "REQUESTED",
                RequestedByUserId = userId,
                RequestedAt = now
            };
            _clinicWorkspace.AddDeferredAction(created);
            return created.DeferredActionId;
        }

        open.DeferReason = deferReason;
        open.DeferUntil = request.DeferUntil;
        open.ActionTypeId = actionType.ActionTypeId;
        open.TargetClinicId = actionType.RequiresTargetClinic ? request.TargetClinicId : null;
        open.ActionDescription = actionDescription;
        open.FollowupNote = followupNote;
        open.Status = "REQUESTED";
        open.CompletedByUserId = null;
        open.CompletedAt = null;
        open.ClosedAt = null;
        return open.DeferredActionId;
    }

    private async Task CloseOpenDeferredActionsAsync(
        Guid assessmentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var openActions = await _clinicWorkspace.QueryDeferredActions(asNoTracking: false)
            .Where(d => d.ClinicAssessmentId == assessmentId && d.Status != "CLOSED")
            .ToListAsync(cancellationToken);

        // FR-042: لا تُعاد التقييم خارج المؤجل قبل إتمام الإجراء المطلوب.
        if (openActions.Any(a => string.Equals(a.Status, "REQUESTED", StringComparison.OrdinalIgnoreCase)))
            throw new DeferredActionNotCompletedException();

        foreach (var action in openActions)
        {
            action.Status = "CLOSED";
            action.ClosedAt = action.ClosedAt ?? now;
        }
    }

    #endregion

    #region Auth / lock / audit

    private async Task EnsureDoctorClinicAsync(
        Guid centerId,
        Guid clinicId,
        string userId,
        CancellationToken cancellationToken)
    {
        await DoctorClinicAccess.RequireActiveAssignmentAsync(
            _users, _centerClinics, centerId, clinicId, userId, cancellationToken);
    }

    private static bool IsOwnedActiveLock(
        ClinicExamEditLock? editLock,
        string userId,
        DateTimeOffset now)
        => editLock is not null
            && editLock.ExpiresAt > now
            && string.Equals(editLock.LockedByUserId, userId, StringComparison.Ordinal);

    private void RecordAssessmentSavedAudit(
        string userId,
        Guid centerId,
        Guid memberId,
        Guid clinicId,
        ClinicAssessment assessment)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = AssessmentSavedEventType,
            Result = "SUCCESS",
            EntityType = nameof(ClinicAssessment),
            EntityId = assessment.AssessmentId.ToString(),
            ReasonText =
                $"حفظ تقييم عيادة {clinicId:D} بنتيجة {assessment.ResultStatusId} للمنتسب {memberId:D}",
            OldValues = null,
            NewValues = null,
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });
    }

    private async Task RecordConflictAuditAsync(
        string userId,
        Guid centerId,
        Guid memberId,
        Guid clinicId,
        bool succeeded,
        string reason,
        CancellationToken cancellationToken)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = ExamConflictEventType,
            Result = succeeded ? "SUCCESS" : "FAILURE",
            EntityType = nameof(MedicalExam),
            EntityId = memberId.ToString(),
            ReasonText = $"{reason} العيادة={clinicId:D}",
            OldValues = null,
            NewValues = null,
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private string RequireUserId()
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
            throw new UnauthenticatedUserException();
        return _currentUser.UserId;
    }

    #endregion
}
