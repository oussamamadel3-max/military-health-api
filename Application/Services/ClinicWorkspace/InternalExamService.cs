using System.Net;
using Application.Common.Permissions;
using Application.Common.ReferenceData;
using Application.Common.Results;
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
/// حفظ وعرض وفهرسة فحص الباطنة مع تقييم العيادة
/// (FR-035 / شريحة قائمة وعرض وتعديل FR-039 لعيادة الباطنة).
/// </summary>
public class InternalExamService : IInternalExamService
{
    public const string InternalExamSavedEventType = "InternalExamSaved";
    public const string ExamConflictEventType = ClinicAssessmentService.ExamConflictEventType;
    public const string InternalClinicCode = ExamClinicCatalog.InternalCode;

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

    public InternalExamService(
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
    public async Task<InternalExamDto> SaveAsync(
        Guid memberId,
        SaveInternalExamRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new InvalidInternalExamDataException("بيانات فحص الباطنة مطلوبة.");

        if (memberId == Guid.Empty)
            throw new InvalidInternalExamDataException("معرّف المنتسب مطلوب.");

        if (request.ClinicId == Guid.Empty)
            throw new InvalidInternalExamDataException("معرّف العيادة مطلوب.");

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
        await EnsureDoctorInternalClinicAsync(centerId, request.ClinicId, userId, cancellationToken);
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
                reason: "رفض حفظ فحص الباطنة بسبب غياب قفل التحرير أو ملكيته لطبيب آخر أو انتهاء المهلة.",
                cancellationToken);
            throw new ClinicExamEditLockConflictException();
        }

        ValidateInternalDetailPayload(request);
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

            var (assessment, previousStatus) = await UpsertAssessmentCoreAsync(
                exam,
                memberId,
                centerId,
                request,
                userId,
                now,
                cancellationToken);

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

            await UpsertInternalDetailsAsync(exam.ExamId, request, cancellationToken);

            _clinicWorkspace.RemoveEditLock(editLock!);
            RecordInternalExamSavedAudit(userId, centerId, memberId, request.ClinicId, exam);

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
    public async Task<InternalExamDto> GetAsync(
        Guid memberId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (memberId == Guid.Empty)
            throw new InvalidInternalExamDataException("معرّف المنتسب مطلوب.");

        if (clinicId == Guid.Empty)
            throw new InvalidInternalExamDataException("معرّف العيادة مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.View,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        await EnsureDoctorInternalClinicAsync(centerId, clinicId, userId, cancellationToken);
        await _members.GetByIdAsync(memberId, clientCenterId, cancellationToken);

        var exam = await _clinicWorkspace.QueryMedicalExams()
            .FirstOrDefaultAsync(
                e => e.MemberId == memberId
                    && e.ClinicId == clinicId
                    && e.IsCurrent
                    && e.CenterId == centerId,
                cancellationToken);

        if (exam is null)
            throw new InternalExamNotFoundException();

        var detail = await _clinicWorkspace.QueryInternalExamDetails()
            .FirstOrDefaultAsync(d => d.ExamId == exam.ExamId, cancellationToken);

        if (detail is null)
            throw new InternalExamNotFoundException();

        return await BuildDtoAsync(exam.ExamId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PaginatedList<InternalExamListItemDto>> ListAsync(
        ClinicExamListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var (fileNumber, pageNumber, pageSize) = ClinicExamListSupport.NormalizeListQuery(
            query,
            message => new InvalidInternalExamDataException(message));

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.View,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        await EnsureDoctorInternalClinicAsync(centerId, query.ClinicId, userId, cancellationToken);

        var source = _clinicWorkspace.QueryInternalExamDetails()
            .Where(d => d.Exam.CenterId == centerId
                && d.Exam.ClinicId == query.ClinicId
                && d.Exam.IsCurrent);

        if (fileNumber.HasValue)
            source = source.Where(d => d.Exam.Member.FileNumber == fileNumber.Value);

        if (!string.IsNullOrWhiteSpace(query.RecruitmentNumber))
        {
            var recruitment = query.RecruitmentNumber.Trim();
            source = source.Where(d => d.Exam.Member.RecruitmentNumber == recruitment);
        }

        if (!string.IsNullOrWhiteSpace(query.FullName))
        {
            var name = query.FullName.Trim();
            source = source.Where(d => d.Exam.Member.FullName.Contains(name));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var rows = await source
            .OrderByDescending(d => d.Exam.ExaminedAt)
            .ThenByDescending(d => d.Exam.Member.FileNumber)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new
            {
                d.ExamId,
                d.Exam.MemberId,
                d.Exam.Member.FileNumber,
                d.Exam.Member.FullName,
                d.Cardiovascular,
                d.Respiratory,
                d.Digestive,
                d.Endocrine,
                d.Neurological,
                d.Hematology,
                d.Joints,
                d.Renal,
                d.Skin,
                d.Exam.ExaminerUserId,
                ExaminerUserName = d.Exam.ExaminerUser.FullName ?? d.Exam.ExaminerUser.UserName,
                d.Exam.ExaminedAt
            })
            .ToListAsync(cancellationToken);

        var assessments = await LoadAssessmentNamesAsync(
            rows.Select(r => r.ExamId).ToList(),
            cancellationToken);

        var items = rows.Select(r =>
        {
            short? resultStatusId = null;
            string? resultStatusNameAr = null;
            if (assessments.TryGetValue(r.ExamId, out var assessment))
            {
                resultStatusId = assessment.ResultStatusId;
                resultStatusNameAr = assessment.NameAr;
            }

            return new InternalExamListItemDto
            {
                ExamId = r.ExamId,
                MemberId = r.MemberId,
                FileNumber = r.FileNumber,
                FullName = r.FullName,
                Cardiovascular = r.Cardiovascular,
                Respiratory = r.Respiratory,
                Digestive = r.Digestive,
                Endocrine = r.Endocrine,
                Neurological = r.Neurological,
                Hematology = r.Hematology,
                Joints = r.Joints,
                Renal = r.Renal,
                Skin = r.Skin,
                ResultStatusId = resultStatusId,
                ResultStatusNameAr = resultStatusNameAr,
                ExaminerUserId = r.ExaminerUserId,
                ExaminerUserName = r.ExaminerUserName,
                ExaminedAt = r.ExaminedAt
            };
        }).ToList();

        return new PaginatedList<InternalExamListItemDto>(items, totalCount, pageNumber, pageSize);
    }

    private async Task<Dictionary<Guid, (short ResultStatusId, string NameAr)>> LoadAssessmentNamesAsync(
        IReadOnlyList<Guid> examIds,
        CancellationToken cancellationToken)
    {
        if (examIds.Count == 0)
            return new Dictionary<Guid, (short, string)>();

        return await _clinicWorkspace.QueryClinicAssessments()
            .Where(a => examIds.Contains(a.ExamId))
            .Select(a => new { a.ExamId, a.ResultStatusId, NameAr = a.ResultStatus.NameAr })
            .ToDictionaryAsync(
                a => a.ExamId,
                a => (a.ResultStatusId, a.NameAr),
                cancellationToken);
    }

    #region Internal detail

    private async Task UpsertInternalDetailsAsync(
        Guid examId,
        SaveInternalExamRequest request,
        CancellationToken cancellationToken)
    {
        var detail = await _clinicWorkspace.QueryInternalExamDetails(asNoTracking: false)
            .FirstOrDefaultAsync(d => d.ExamId == examId, cancellationToken);

        if (detail is null)
        {
            detail = new InternalExamDetail
            {
                ExamId = examId,
                Cardiovascular = request.Cardiovascular.Trim(),
                Respiratory = request.Respiratory.Trim(),
                Digestive = request.Digestive.Trim(),
                Endocrine = request.Endocrine.Trim(),
                Neurological = request.Neurological.Trim(),
                Hematology = request.Hematology.Trim(),
                Joints = request.Joints.Trim(),
                Renal = request.Renal.Trim(),
                Skin = request.Skin.Trim()
            };
            _clinicWorkspace.AddInternalExamDetail(detail);
        }
        else
        {
            detail.Cardiovascular = request.Cardiovascular.Trim();
            detail.Respiratory = request.Respiratory.Trim();
            detail.Digestive = request.Digestive.Trim();
            detail.Endocrine = request.Endocrine.Trim();
            detail.Neurological = request.Neurological.Trim();
            detail.Hematology = request.Hematology.Trim();
            detail.Joints = request.Joints.Trim();
            detail.Renal = request.Renal.Trim();
            detail.Skin = request.Skin.Trim();
        }
    }

    private static void ValidateInternalDetailPayload(SaveInternalExamRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Cardiovascular))
            throw new InvalidInternalExamDataException("نتيجة فحص القلب إلزامية.");

        if (string.IsNullOrWhiteSpace(request.Respiratory))
            throw new InvalidInternalExamDataException("نتيجة فحص التنفس إلزامية.");

        if (string.IsNullOrWhiteSpace(request.Digestive))
            throw new InvalidInternalExamDataException("نتيجة فحص الهضم إلزامية.");

        if (string.IsNullOrWhiteSpace(request.Endocrine))
            throw new InvalidInternalExamDataException("نتيجة فحص الغدد إلزامية.");

        if (string.IsNullOrWhiteSpace(request.Neurological))
            throw new InvalidInternalExamDataException("نتيجة فحص الأعصاب إلزامية.");

        if (string.IsNullOrWhiteSpace(request.Hematology))
            throw new InvalidInternalExamDataException("نتيجة فحص الدم إلزامية.");

        if (string.IsNullOrWhiteSpace(request.Joints))
            throw new InvalidInternalExamDataException("نتيجة فحص المفاصل إلزامية.");

        if (string.IsNullOrWhiteSpace(request.Renal))
            throw new InvalidInternalExamDataException("نتيجة فحص الكلية إلزامية.");

        if (string.IsNullOrWhiteSpace(request.Skin))
            throw new InvalidInternalExamDataException("نتيجة فحص الجلد إلزامية.");
    }

    #endregion

    #region Assessment (shared rules with ClinicAssessmentService)

    private async Task<(ClinicAssessment Assessment, short? PreviousStatus)> UpsertAssessmentCoreAsync(
        MedicalExam exam,
        Guid memberId,
        Guid centerId,
        SaveInternalExamRequest request,
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

    private static void ValidateAssessmentPayload(SaveInternalExamRequest request)
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
        SaveInternalExamRequest request,
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
        SaveInternalExamRequest request,
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

    #region Mapping / auth / audit

    private async Task<InternalExamDto> BuildDtoAsync(Guid examId, CancellationToken cancellationToken)
    {
        var exam = await _clinicWorkspace.QueryMedicalExams()
            .FirstAsync(e => e.ExamId == examId, cancellationToken);

        var detail = await _clinicWorkspace.QueryInternalExamDetails()
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

        return new InternalExamDto
        {
            ExamId = exam.ExamId,
            MemberId = exam.MemberId,
            CenterId = exam.CenterId,
            ClinicId = exam.ClinicId,
            ExaminerUserId = exam.ExaminerUserId,
            ExaminedAt = exam.ExaminedAt,
            ExamVersionNo = exam.VersionNo,
            Cardiovascular = detail.Cardiovascular,
            Respiratory = detail.Respiratory,
            Digestive = detail.Digestive,
            Endocrine = detail.Endocrine,
            Neurological = detail.Neurological,
            Hematology = detail.Hematology,
            Joints = detail.Joints,
            Renal = detail.Renal,
            Skin = detail.Skin,
            AssessmentId = assessment?.AssessmentId,
            DeferredActionId = deferredActionId,
            ResultStatusId = assessment?.ResultStatusId,
            RejectionReason = assessment?.RejectionReason,
            ClinicalNotes = assessment?.ClinicalNotes,
            AssessedByUserId = assessment?.AssessedByUserId,
            AssessedAt = assessment?.AssessedAt
        };
    }

    private async Task EnsureDoctorInternalClinicAsync(
        Guid centerId,
        Guid clinicId,
        string userId,
        CancellationToken cancellationToken)
    {
        var link = await DoctorClinicAccess.RequireActiveAssignmentAsync(
            _users, _centerClinics, centerId, clinicId, userId, cancellationToken);

        if (!string.Equals(link.Clinic.Code, InternalClinicCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidInternalExamDataException(
                ExamClinicCatalog.FormatExamMismatch(link.Clinic.Code, InternalClinicCode));
    }

    private static bool IsOwnedActiveLock(
        ClinicExamEditLock? editLock,
        string userId,
        DateTimeOffset now)
        => editLock is not null
            && editLock.ExpiresAt > now
            && string.Equals(editLock.LockedByUserId, userId, StringComparison.Ordinal);

    private void RecordInternalExamSavedAudit(
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
            EventType = InternalExamSavedEventType,
            Result = "SUCCESS",
            EntityType = nameof(InternalExamDetail),
            EntityId = exam.ExamId.ToString(),
            ReasonText =
                $"حفظ فحص باطنة {clinicId:D} إصدار {exam.VersionNo} للمنتسب {memberId:D}",
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
