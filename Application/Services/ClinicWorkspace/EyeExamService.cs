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
/// حفظ وعرض فحص العيون والانكسار مع تقييم العيادة في معاملة واحدة
/// (FR-033 / FR-034 / شريحة FR-039 لعيادة العيون).
/// </summary>
public class EyeExamService : IEyeExamService
{
    public const string EyeExamSavedEventType = "EyeExamSaved";
    public const string ExamConflictEventType = ClinicAssessmentService.ExamConflictEventType;
    public const string EyeClinicCode = "EYE";

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

    public EyeExamService(
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
    public async Task<EyeExamDto> SaveAsync(
        Guid memberId,
        SaveEyeExamRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new InvalidEyeExamDataException("بيانات فحص العيون مطلوبة.");

        if (memberId == Guid.Empty)
            throw new InvalidEyeExamDataException("معرّف المنتسب مطلوب.");

        if (request.ClinicId == Guid.Empty)
            throw new InvalidEyeExamDataException("معرّف العيادة مطلوب.");

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
        await EnsureDoctorEyeClinicAsync(centerId, request.ClinicId, userId, cancellationToken);
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
                reason: "رفض حفظ فحص العيون بسبب غياب قفل التحرير أو ملكيته لطبيب آخر أو انتهاء المهلة.",
                cancellationToken);
            throw new ClinicExamEditLockConflictException();
        }

        ValidateEyeDetailPayload(request);
        ValidateAssessmentPayload(request);
        var normalizedRefractions = NormalizeAndValidateRefractions(request.Refractions);
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

            await UpsertEyeDetailsAsync(exam.ExamId, request, normalizedRefractions, cancellationToken);

            _clinicWorkspace.RemoveEditLock(editLock!);
            RecordEyeExamSavedAudit(userId, centerId, memberId, request.ClinicId, exam);

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
    public async Task<EyeExamDto> GetAsync(
        Guid memberId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (memberId == Guid.Empty)
            throw new InvalidEyeExamDataException("معرّف المنتسب مطلوب.");

        if (clinicId == Guid.Empty)
            throw new InvalidEyeExamDataException("معرّف العيادة مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.View,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        await EnsureDoctorEyeClinicAsync(centerId, clinicId, userId, cancellationToken);
        await _members.GetByIdAsync(memberId, clientCenterId, cancellationToken);

        var exam = await _clinicWorkspace.QueryMedicalExams()
            .FirstOrDefaultAsync(
                e => e.MemberId == memberId
                    && e.ClinicId == clinicId
                    && e.IsCurrent
                    && e.CenterId == centerId,
                cancellationToken);

        if (exam is null)
            throw new EyeExamNotFoundException();

        var eyeDetail = await _clinicWorkspace.QueryEyeExamDetails()
            .FirstOrDefaultAsync(d => d.ExamId == exam.ExamId, cancellationToken);

        if (eyeDetail is null)
            throw new EyeExamNotFoundException();

        return await BuildDtoAsync(exam.ExamId, cancellationToken);
    }

    #region Eye detail / refraction

    private async Task UpsertEyeDetailsAsync(
        Guid examId,
        SaveEyeExamRequest request,
        IReadOnlyList<EyeRefractionInputDto> normalizedRefractions,
        CancellationToken cancellationToken)
    {
        var detail = await _clinicWorkspace.QueryEyeExamDetails(asNoTracking: false)
            .FirstOrDefaultAsync(d => d.ExamId == examId, cancellationToken);

        var otherDiseases = string.IsNullOrWhiteSpace(request.OtherEyeDiseases)
            ? null
            : request.OtherEyeDiseases.Trim();

        if (detail is null)
        {
            detail = new EyeExamDetail
            {
                ExamId = examId,
                VisualAcuityRight = request.VisualAcuityRight.Trim(),
                VisualAcuityLeft = request.VisualAcuityLeft.Trim(),
                ColorTestResult = request.ColorTestResult.Trim(),
                OtherEyeDiseases = otherDiseases
            };
            _clinicWorkspace.AddEyeExamDetail(detail);
        }
        else
        {
            detail.VisualAcuityRight = request.VisualAcuityRight.Trim();
            detail.VisualAcuityLeft = request.VisualAcuityLeft.Trim();
            detail.ColorTestResult = request.ColorTestResult.Trim();
            detail.OtherEyeDiseases = otherDiseases;
        }

        var existingRefractions = await _clinicWorkspace.QueryEyeRefractionDetails(asNoTracking: false)
            .Where(r => r.ExamId == examId)
            .ToListAsync(cancellationToken);

        var existingById = existingRefractions.ToDictionary(r => r.RefractionDetailId);
        var requestedExistingIds = normalizedRefractions
            .Where(r => r.RefractionDetailId.HasValue)
            .Select(r => r.RefractionDetailId!.Value)
            .ToHashSet();

        foreach (var existing in existingRefractions)
        {
            if (!requestedExistingIds.Contains(existing.RefractionDetailId))
            {
                throw new InvalidEyeExamDataException(
                    "لا يمكن حذف قيم انكسار محفوظة؛ أرسل جميع السجلات الحالية مع معرّفاتها.");
            }
        }

        foreach (var input in normalizedRefractions)
        {
            if (input.RefractionDetailId.HasValue)
            {
                if (!existingById.TryGetValue(input.RefractionDetailId.Value, out var row))
                {
                    throw new InvalidEyeExamDataException(
                        "معرّف الانكسار غير موجود ضمن فحص العيون الحالي.");
                }

                ApplyRefractionValues(row, input);
            }
            else
            {
                var created = new EyeRefractionDetail
                {
                    RefractionDetailId = Guid.NewGuid(),
                    ExamId = examId
                };
                ApplyRefractionValues(created, input);
                _clinicWorkspace.AddEyeRefractionDetail(created);
            }
        }
    }

    private static void ApplyRefractionValues(EyeRefractionDetail row, EyeRefractionInputDto input)
    {
        row.EyeSide = input.EyeSide;
        row.RefractionType = input.RefractionType;
        row.InputFormat = input.InputFormat;
        row.Numerator = input.Numerator;
        row.Denominator = input.Denominator;
        row.DecimalInput = input.DecimalInput;
        row.IsWorst = input.IsWorst;
        // CalculatedValue عمود محسوب في PostgreSQL؛ يُشتق في DTO عند العرض لبيئة الاختبار أيضاً.
    }

    private static void ValidateEyeDetailPayload(SaveEyeExamRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VisualAcuityRight))
            throw new InvalidEyeExamDataException("القدرة البصرية للعين اليمنى إلزامية.");

        if (string.IsNullOrWhiteSpace(request.VisualAcuityLeft))
            throw new InvalidEyeExamDataException("القدرة البصرية للعين اليسرى إلزامية.");

        if (string.IsNullOrWhiteSpace(request.ColorTestResult))
            throw new InvalidEyeExamDataException("نتيجة اختبار الألوان إلزامية.");
    }

    private static IReadOnlyList<EyeRefractionInputDto> NormalizeAndValidateRefractions(
        IReadOnlyList<EyeRefractionInputDto>? refractions)
    {
        var list = refractions ?? Array.Empty<EyeRefractionInputDto>();
        var normalized = new List<EyeRefractionInputDto>(list.Count);

        foreach (var raw in list)
        {
            if (raw is null)
                throw new InvalidEyeExamDataException("عنصر انكسار غير صالح.");

            var eyeSide = (raw.EyeSide ?? string.Empty).Trim().ToUpperInvariant();
            if (eyeSide is not (EyeSides.Right or EyeSides.Left))
                throw new InvalidEyeExamDataException("جانب العين يجب أن يكون RIGHT أو LEFT.");

            var refractionType = (raw.RefractionType ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(refractionType))
                throw new InvalidEyeExamDataException("نوع الانكسار إلزامي.");

            var inputFormat = (raw.InputFormat ?? string.Empty).Trim().ToUpperInvariant();
            if (inputFormat is not (EyeRefractionInputFormats.Fraction or EyeRefractionInputFormats.Decimal))
                throw new InvalidEyeExamDataException("صيغة إدخال الانكسار يجب أن تكون FRACTION أو DECIMAL.");

            decimal? numerator = null;
            decimal? denominator = null;
            decimal? decimalInput = null;

            if (inputFormat == EyeRefractionInputFormats.Fraction)
            {
                if (!raw.Numerator.HasValue)
                    throw new InvalidEyeExamDataException("البسط إلزامي عند الصيغة الكسرية.");

                if (!raw.Denominator.HasValue)
                    throw new InvalidEyeExamDataException("المقام إلزامي عند الصيغة الكسرية.");

                if (raw.Denominator.Value == 0)
                    throw new InvalidEyeExamDataException("لا يُقبل مقام صفري في قيمة الانكسار.");

                if (raw.Denominator.Value < 0)
                    throw new InvalidEyeExamDataException("المقام يجب أن يكون أكبر من صفر.");

                if (raw.DecimalInput.HasValue)
                    throw new InvalidEyeExamDataException("القيمة العشرية غير مسموحة مع الصيغة الكسرية.");

                numerator = raw.Numerator;
                denominator = raw.Denominator;
            }
            else
            {
                if (!raw.DecimalInput.HasValue)
                    throw new InvalidEyeExamDataException("القيمة العشرية إلزامية عند الصيغة العشرية.");

                if (raw.Numerator.HasValue || raw.Denominator.HasValue)
                    throw new InvalidEyeExamDataException("البسط والمقام غير مسموحين مع الصيغة العشرية.");

                decimalInput = raw.DecimalInput;
            }

            if (raw.RefractionDetailId.HasValue && raw.RefractionDetailId.Value == Guid.Empty)
                throw new InvalidEyeExamDataException("معرّف الانكسار غير صالح.");

            normalized.Add(new EyeRefractionInputDto
            {
                RefractionDetailId = raw.RefractionDetailId,
                EyeSide = eyeSide,
                RefractionType = refractionType,
                InputFormat = inputFormat,
                Numerator = numerator,
                Denominator = denominator,
                DecimalInput = decimalInput,
                IsWorst = raw.IsWorst
            });
        }

        // أسوأ انكسار واحد بالضبط لكل عين لديها قيم.
        foreach (var side in new[] { EyeSides.Right, EyeSides.Left })
        {
            var sideRows = normalized.Where(r => r.EyeSide == side).ToList();
            if (sideRows.Count == 0)
                continue;

            var worstCount = sideRows.Count(r => r.IsWorst);
            if (worstCount != 1)
            {
                throw new InvalidEyeExamDataException(
                    $"يجب تحديد أسوأ انكسار واحد بالضبط للعين {(side == EyeSides.Right ? "اليمنى" : "اليسرى")}.");
            }
        }

        // توافق فهرس isWorst الفريد لكل (عين، نوع).
        var worstBySideType = normalized
            .Where(r => r.IsWorst)
            .GroupBy(r => (r.EyeSide, Type: r.RefractionType.ToUpperInvariant()))
            .FirstOrDefault(g => g.Count() > 1);

        if (worstBySideType is not null)
        {
            throw new InvalidEyeExamDataException(
                "لا يجوز تعيين أكثر من أسوأ انكسار لنفس العين ونوع الانكسار.");
        }

        var duplicateIds = normalized
            .Where(r => r.RefractionDetailId.HasValue)
            .GroupBy(r => r.RefractionDetailId!.Value)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateIds is not null)
            throw new InvalidEyeExamDataException("تكرار معرّف انكسار في الطلب غير مسموح.");

        return normalized;
    }

    #endregion

    #region Assessment (shared rules with ClinicAssessmentService)

    private async Task<(ClinicAssessment Assessment, short? PreviousStatus)> UpsertAssessmentCoreAsync(
        MedicalExam exam,
        Guid memberId,
        Guid centerId,
        SaveEyeExamRequest request,
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

    private static void ValidateAssessmentPayload(SaveEyeExamRequest request)
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
        SaveEyeExamRequest request,
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
        SaveEyeExamRequest request,
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

    private async Task<EyeExamDto> BuildDtoAsync(Guid examId, CancellationToken cancellationToken)
    {
        var exam = await _clinicWorkspace.QueryMedicalExams()
            .FirstAsync(e => e.ExamId == examId, cancellationToken);

        var detail = await _clinicWorkspace.QueryEyeExamDetails()
            .FirstAsync(d => d.ExamId == examId, cancellationToken);

        var refractions = await _clinicWorkspace.QueryEyeRefractionDetails()
            .Where(r => r.ExamId == examId)
            .OrderBy(r => r.EyeSide)
            .ThenBy(r => r.RefractionType)
            .ToListAsync(cancellationToken);

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

        return new EyeExamDto
        {
            ExamId = exam.ExamId,
            MemberId = exam.MemberId,
            CenterId = exam.CenterId,
            ClinicId = exam.ClinicId,
            ExaminerUserId = exam.ExaminerUserId,
            ExaminedAt = exam.ExaminedAt,
            ExamVersionNo = exam.VersionNo,
            VisualAcuityRight = detail.VisualAcuityRight,
            VisualAcuityLeft = detail.VisualAcuityLeft,
            ColorTestResult = detail.ColorTestResult,
            OtherEyeDiseases = detail.OtherEyeDiseases,
            Refractions = refractions.Select(MapRefraction).ToList(),
            AssessmentId = assessment?.AssessmentId,
            DeferredActionId = deferredActionId,
            ResultStatusId = assessment?.ResultStatusId,
            RejectionReason = assessment?.RejectionReason,
            ClinicalNotes = assessment?.ClinicalNotes,
            AssessedByUserId = assessment?.AssessedByUserId,
            AssessedAt = assessment?.AssessedAt
        };
    }

    private static EyeRefractionDetailDto MapRefraction(EyeRefractionDetail row)
    {
        var calculated = row.CalculatedValue
            ?? (string.Equals(row.InputFormat, EyeRefractionInputFormats.Fraction, StringComparison.OrdinalIgnoreCase)
                && row.Numerator.HasValue
                && row.Denominator is > 0
                    ? row.Numerator.Value / row.Denominator.Value
                    : row.DecimalInput);

        return new EyeRefractionDetailDto
        {
            RefractionDetailId = row.RefractionDetailId,
            EyeSide = row.EyeSide,
            RefractionType = row.RefractionType,
            InputFormat = row.InputFormat,
            Numerator = row.Numerator,
            Denominator = row.Denominator,
            DecimalInput = row.DecimalInput,
            CalculatedValue = calculated,
            IsWorst = row.IsWorst
        };
    }

    private async Task EnsureDoctorEyeClinicAsync(
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

        if (!string.Equals(link.Clinic.Code, EyeClinicCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidEyeExamDataException("فحص العيون مسموح فقط لعيادة العيون.");
    }

    private static bool IsOwnedActiveLock(
        ClinicExamEditLock? editLock,
        string userId,
        DateTimeOffset now)
        => editLock is not null
            && editLock.ExpiresAt > now
            && string.Equals(editLock.LockedByUserId, userId, StringComparison.Ordinal);

    private void RecordEyeExamSavedAudit(
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
            EventType = EyeExamSavedEventType,
            Result = "SUCCESS",
            EntityType = nameof(EyeExamDetail),
            EntityId = exam.ExamId.ToString(),
            ReasonText =
                $"حفظ فحص عيون {clinicId:D} إصدار {exam.VersionNo} للمنتسب {memberId:D}",
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
