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
/// حفظ وعرض فحص الجراحة مع تقييم العيادة في معاملة واحدة
/// (FR-036 / شريحة FR-039 لعيادة الجراحة).
/// </summary>
public class SurgeryExamService : ISurgeryExamService
{
    public const string SurgeryExamSavedEventType = "SurgeryExamSaved";
    public const string ExamConflictEventType = ClinicAssessmentService.ExamConflictEventType;
    public const string SurgeryClinicCode = "SURGERY";

    public const short AcceptedResultStatusId = ClinicAssessmentService.AcceptedResultStatusId;
    public const short RejectedResultStatusId = ClinicAssessmentService.RejectedResultStatusId;
    public const short DeferredResultStatusId = ClinicAssessmentService.DeferredResultStatusId;

    private readonly IClinicWorkspaceRepository _clinicWorkspace;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IUserRepository _users;
    private readonly ICenterClinicRepository _centerClinics;
    private readonly IMemberService _members;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;

    public SurgeryExamService(
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
    public async Task<SurgeryExamDto> SaveAsync(
        Guid memberId,
        SaveSurgeryExamRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new InvalidSurgeryExamDataException("بيانات فحص الجراحة مطلوبة.");

        if (memberId == Guid.Empty)
            throw new InvalidSurgeryExamDataException("معرّف المنتسب مطلوب.");

        if (request.ClinicId == Guid.Empty)
            throw new InvalidSurgeryExamDataException("معرّف العيادة مطلوب.");

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
        await EnsureDoctorSurgeryClinicAsync(centerId, request.ClinicId, userId, cancellationToken);
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
                reason: "رفض حفظ فحص الجراحة بسبب غياب قفل التحرير أو ملكيته لطبيب آخر أو انتهاء المهلة.",
                cancellationToken);
            throw new ClinicExamEditLockConflictException();
        }

        ValidateSurgeryDetailPayload(request);
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

                exam.ExaminerUserId = userId;
                exam.ExaminedAt = now;
                exam.VersionNo += 1;
                exam.UpdatedByUserId = userId;
                exam.UpdatedAt = now;
            }

            var (assessment, previousStatus) = await UpsertAssessmentCoreAsync(
                exam,
                memberId,
                centerId,
                request,
                userId,
                now,
                cancellationToken);

            if (isDeferred)
            {
                await UpsertDeferredActionAsync(
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

            await UpsertSurgeryDetailsAsync(exam.ExamId, request, cancellationToken);

            _clinicWorkspace.RemoveEditLock(editLock!);
            RecordSurgeryExamSavedAudit(userId, centerId, memberId, request.ClinicId, exam);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return await BuildDtoAsync(exam.ExamId, cancellationToken);
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
    public async Task<SurgeryExamDto> GetAsync(
        Guid memberId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (memberId == Guid.Empty)
            throw new InvalidSurgeryExamDataException("معرّف المنتسب مطلوب.");

        if (clinicId == Guid.Empty)
            throw new InvalidSurgeryExamDataException("معرّف العيادة مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.View,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        await EnsureDoctorSurgeryClinicAsync(centerId, clinicId, userId, cancellationToken);
        await _members.GetByIdAsync(memberId, clientCenterId, cancellationToken);

        var exam = await _clinicWorkspace.QueryMedicalExams()
            .FirstOrDefaultAsync(
                e => e.MemberId == memberId
                    && e.ClinicId == clinicId
                    && e.IsCurrent
                    && e.CenterId == centerId,
                cancellationToken);

        if (exam is null)
            throw new SurgeryExamNotFoundException();

        var detail = await _clinicWorkspace.QuerySurgeryExamDetails()
            .FirstOrDefaultAsync(d => d.ExamId == exam.ExamId, cancellationToken);

        if (detail is null)
            throw new SurgeryExamNotFoundException();

        return await BuildDtoAsync(exam.ExamId, cancellationToken);
    }

    #region Surgery detail

    private async Task UpsertSurgeryDetailsAsync(
        Guid examId,
        SaveSurgeryExamRequest request,
        CancellationToken cancellationToken)
    {
        var detail = await _clinicWorkspace.QuerySurgeryExamDetails(asNoTracking: false)
            .FirstOrDefaultAsync(d => d.ExamId == examId, cancellationToken);

        if (detail is null)
        {
            detail = new SurgeryExamDetail
            {
                ExamId = examId,
                GeneralSurgery = request.GeneralSurgery.Trim(),
                Urology = request.Urology.Trim(),
                VascularThoracic = request.VascularThoracic.Trim()
            };
            _clinicWorkspace.AddSurgeryExamDetail(detail);
        }
        else
        {
            detail.GeneralSurgery = request.GeneralSurgery.Trim();
            detail.Urology = request.Urology.Trim();
            detail.VascularThoracic = request.VascularThoracic.Trim();
        }
    }

    private static void ValidateSurgeryDetailPayload(SaveSurgeryExamRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GeneralSurgery))
            throw new InvalidSurgeryExamDataException("نتيجة الجراحة العامة إلزامية.");

        if (string.IsNullOrWhiteSpace(request.Urology))
            throw new InvalidSurgeryExamDataException("نتيجة الجراحة البولية إلزامية.");

        if (string.IsNullOrWhiteSpace(request.VascularThoracic))
            throw new InvalidSurgeryExamDataException("نتيجة الجراحة الوعائية والصدرية إلزامية.");
    }

    #endregion

    #region Assessment (shared rules with ClinicAssessmentService)

    private async Task<(ClinicAssessment Assessment, short? PreviousStatus)> UpsertAssessmentCoreAsync(
        MedicalExam exam,
        Guid memberId,
        Guid centerId,
        SaveSurgeryExamRequest request,
        string userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
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

        return (assessment, previousStatus);
    }

    private static void ValidateAssessmentPayload(SaveSurgeryExamRequest request)
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
        SaveSurgeryExamRequest request,
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

    private async Task<Guid> UpsertDeferredActionAsync(
        ClinicAssessment assessment,
        SaveSurgeryExamRequest request,
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
        open.RequestedByUserId = userId;
        open.RequestedAt = now;
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

        foreach (var action in openActions)
        {
            action.Status = "CLOSED";
            action.ClosedAt = action.ClosedAt ?? now;
        }
    }

    #endregion

    #region Mapping / auth / audit

    private async Task<SurgeryExamDto> BuildDtoAsync(Guid examId, CancellationToken cancellationToken)
    {
        var exam = await _clinicWorkspace.QueryMedicalExams()
            .FirstAsync(e => e.ExamId == examId, cancellationToken);

        var detail = await _clinicWorkspace.QuerySurgeryExamDetails()
            .FirstAsync(d => d.ExamId == examId, cancellationToken);

        var assessment = await _clinicWorkspace.QueryClinicAssessments()
            .FirstOrDefaultAsync(a => a.ExamId == examId, cancellationToken);

        Guid? deferredActionId = null;
        if (assessment is not null && assessment.ResultStatusId == DeferredResultStatusId)
        {
            deferredActionId = await _clinicWorkspace.QueryDeferredActions()
                .Where(d => d.ClinicAssessmentId == assessment.AssessmentId && d.Status != "CLOSED")
                .OrderByDescending(d => d.RequestedAt)
                .Select(d => (Guid?)d.DeferredActionId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new SurgeryExamDto
        {
            ExamId = exam.ExamId,
            MemberId = exam.MemberId,
            CenterId = exam.CenterId,
            ClinicId = exam.ClinicId,
            ExaminerUserId = exam.ExaminerUserId,
            ExaminedAt = exam.ExaminedAt,
            ExamVersionNo = exam.VersionNo,
            GeneralSurgery = detail.GeneralSurgery,
            Urology = detail.Urology,
            VascularThoracic = detail.VascularThoracic,
            AssessmentId = assessment?.AssessmentId,
            DeferredActionId = deferredActionId,
            ResultStatusId = assessment?.ResultStatusId,
            RejectionReason = assessment?.RejectionReason,
            ClinicalNotes = assessment?.ClinicalNotes,
            AssessedByUserId = assessment?.AssessedByUserId,
            AssessedAt = assessment?.AssessedAt
        };
    }

    private async Task EnsureDoctorSurgeryClinicAsync(
        Guid centerId,
        Guid clinicId,
        string userId,
        CancellationToken cancellationToken)
    {
        var assigned = await _users.QueryUserClinicAssignments()
            .AnyAsync(a => a.UserId == userId
                && a.CenterId == centerId
                && a.ClinicId == clinicId
                && a.IsActive
                && a.RevokedAt == null, cancellationToken);

        if (!assigned)
            throw new DoctorClinicNotAssignedException();

        var link = await _centerClinics.GetByCenterAndClinicAsync(
            centerId,
            clinicId,
            asNoTracking: true,
            cancellationToken);

        if (link is null || !link.IsActive)
            throw new DoctorClinicNotAssignedException();

        if (!string.Equals(link.Clinic.Code, SurgeryClinicCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidSurgeryExamDataException("فحص الجراحة مسموح فقط لعيادة الجراحة.");
    }

    private static bool IsOwnedActiveLock(
        ClinicExamEditLock? editLock,
        string userId,
        DateTimeOffset now)
        => editLock is not null
            && editLock.ExpiresAt > now
            && string.Equals(editLock.LockedByUserId, userId, StringComparison.Ordinal);

    private void RecordSurgeryExamSavedAudit(
        string userId,
        Guid centerId,
        Guid memberId,
        Guid clinicId,
        MedicalExam exam)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = SurgeryExamSavedEventType,
            Result = "SUCCESS",
            EntityType = nameof(SurgeryExamDetail),
            EntityId = exam.ExamId.ToString(),
            ReasonText =
                $"حفظ فحص جراحة {clinicId:D} إصدار {exam.VersionNo} للمنتسب {memberId:D}",
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
            Result = "FAILURE",
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
