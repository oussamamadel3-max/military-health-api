using System.Net;
using System.Text.Json;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.ClinicWorkspace;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Domain.Entities.Medical;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.ClinicWorkspace;

/// <summary>
/// إنشاء ومتابعة الاستشارات التخصصية مع عزل المركز وإسناد العيادة والتدقيق اليدوي (FR-046).
/// </summary>
public class ConsultationService : IConsultationService
{
    public const string ConsultationCreatedEventType = "ConsultationCreated";
    public const string ConsultationResultUpdatedEventType = "ConsultationResultUpdated";
    public const string StatusPending = "PENDING";
    public const string StatusCompleted = "COMPLETED";

    private readonly IConsultationRepository _consultations;
    private readonly IMemberRepository _members;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IUserRepository _users;
    private readonly ICenterClinicRepository _centerClinics;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;
    private readonly ITextInputSanitizer _textSanitizer;

    public ConsultationService(
        IConsultationRepository consultations,
        IMemberRepository members,
        IReferenceDataRepository referenceData,
        IUserRepository users,
        ICenterClinicRepository centerClinics,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        IPermissionGate permissionGate,
        ICurrentUserService currentUser,
        ITextInputSanitizer textSanitizer)
    {
        _consultations = consultations;
        _members = members;
        _referenceData = referenceData;
        _users = users;
        _centerClinics = centerClinics;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _permissionGate = permissionGate;
        _currentUser = currentUser;
        _textSanitizer = textSanitizer;
    }

    /// <inheritdoc />
    public async Task<ConsultationDto> CreateAsync(
        Guid memberId,
        CreateConsultationRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new InvalidConsultationDataException("بيانات الاستشارة مطلوبة.");

        if (memberId == Guid.Empty)
            throw new InvalidConsultationDataException("معرّف المنتسب مطلوب.");

        if (request.ClinicId == Guid.Empty)
            throw new InvalidConsultationDataException("معرّف العيادة مطلوب.");

        if (request.ConsultationTypeId <= 0)
            throw new InvalidConsultationDataException("نوع الاستشارة مطلوب.");

        var referralReason = NormalizeRequiredText(request.ReferralReason, "سبب الإحالة مطلوب.");
        var resultText = NormalizeOptionalText(request.ResultText);
        var consultantName = NormalizeOptionalText(request.ConsultantName);
        var consultantUserId = NormalizeOptionalUserId(request.ConsultantUserId);

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        await EnsureDoctorClinicAsync(centerId, request.ClinicId, userId, cancellationToken);
        await EnsureMemberInCenterAsync(memberId, centerId, cancellationToken);
        await EnsureActiveConsultationTypeAsync(request.ConsultationTypeId, cancellationToken);
        await EnsureConsultantUserAsync(consultantUserId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var hasResult = !string.IsNullOrEmpty(resultText);
        var consultation = new Consultation
        {
            ConsultationId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = request.ClinicId,
            RequestedByUserId = userId,
            ConsultantUserId = consultantUserId,
            ConsultantName = consultantName,
            ConsultationTypeId = request.ConsultationTypeId,
            ReferralReason = referralReason,
            Status = hasResult ? StatusCompleted : StatusPending,
            ResultText = hasResult ? resultText : null,
            RequestedAt = now,
            ResultedAt = hasResult ? now : null
        };

        _consultations.Add(consultation);

        RecordAudit(
            userId,
            centerId,
            ConsultationCreatedEventType,
            "SUCCESS",
            consultation.ConsultationId.ToString(),
            $"إنشاء استشارة للمنتسب {memberId:D} في العيادة {request.ClinicId:D}",
            oldValues: null,
            newValues: JsonSerializer.Serialize(new
            {
                consultation.ConsultationId,
                consultation.MemberId,
                consultation.CenterId,
                consultation.ClinicId,
                consultation.ConsultationTypeId,
                consultation.Status,
                consultation.RequestedByUserId,
                HasResult = hasResult
            }));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await MapAsync(consultation, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ConsultationDto> UpdateResultAsync(
        Guid consultationId,
        UpdateConsultationResultRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new InvalidConsultationDataException("بيانات نتيجة الاستشارة مطلوبة.");

        if (consultationId == Guid.Empty)
            throw new InvalidConsultationDataException("معرّف الاستشارة مطلوب.");

        var resultText = NormalizeRequiredText(request.ResultText, "نتيجة الاستشارة مطلوبة.");
        var consultantName = NormalizeOptionalText(request.ConsultantName);
        var consultantUserId = NormalizeOptionalUserId(request.ConsultantUserId);

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        var consultation = await _consultations.GetByIdInCenterAsync(
            consultationId,
            centerId,
            asNoTracking: false,
            cancellationToken) ?? throw new ConsultationNotFoundException();

        await EnsureDoctorClinicAsync(centerId, consultation.ClinicId, userId, cancellationToken);
        await EnsureConsultantUserAsync(consultantUserId, cancellationToken);

        if (!string.Equals(consultation.Status, StatusPending, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidConsultationStateException(
                string.Equals(consultation.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase)
                    ? "لا يمكن إعادة تسجيل نتيجة لاستشارة مكتملة."
                    : "لا يمكن تسجيل نتيجة لاستشارة ملغاة أو غير معلّقة.");
        }

        var oldValues = JsonSerializer.Serialize(new
        {
            consultation.Status,
            consultation.ResultText,
            consultation.ResultedAt,
            consultation.ConsultantUserId,
            consultation.ConsultantName
        });

        var now = DateTimeOffset.UtcNow;
        consultation.ResultText = resultText;
        consultation.ResultedAt = now;
        consultation.Status = StatusCompleted;

        if (consultantUserId is not null || consultantName is not null)
        {
            consultation.ConsultantUserId = consultantUserId ?? consultation.ConsultantUserId;
            consultation.ConsultantName = consultantName ?? consultation.ConsultantName;
        }

        RecordAudit(
            userId,
            centerId,
            ConsultationResultUpdatedEventType,
            "SUCCESS",
            consultation.ConsultationId.ToString(),
            $"تسجيل نتيجة استشارة للمنتسب {consultation.MemberId:D}",
            oldValues,
            JsonSerializer.Serialize(new
            {
                consultation.Status,
                consultation.ResultText,
                consultation.ResultedAt,
                consultation.ConsultantUserId,
                consultation.ConsultantName,
                UpdatedByUserId = userId
            }));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await MapAsync(consultation, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ConsultationDto> GetByIdAsync(
        Guid consultationId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (consultationId == Guid.Empty)
            throw new InvalidConsultationDataException("معرّف الاستشارة مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        var consultation = await _consultations.GetByIdInCenterAsync(
            consultationId,
            centerId,
            asNoTracking: true,
            cancellationToken) ?? throw new ConsultationNotFoundException();

        await EnsureDoctorClinicAsync(centerId, consultation.ClinicId, userId, cancellationToken);
        return await MapAsync(consultation, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConsultationDto>> ListByMemberAsync(
        Guid memberId,
        Guid? clinicId = null,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (memberId == Guid.Empty)
            throw new InvalidConsultationDataException("معرّف المنتسب مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        await EnsureMemberInCenterAsync(memberId, centerId, cancellationToken);

        if (clinicId.HasValue)
        {
            if (clinicId.Value == Guid.Empty)
                throw new InvalidConsultationDataException("معرّف العيادة غير صالح.");

            await EnsureDoctorClinicAsync(centerId, clinicId.Value, userId, cancellationToken);
        }
        else
        {
            await EnsureHasAnyActiveClinicAssignmentAsync(centerId, userId, cancellationToken);
        }

        var query = _consultations.Query(asNoTracking: true)
            .Where(c => c.MemberId == memberId && c.CenterId == centerId);

        if (clinicId.HasValue)
            query = query.Where(c => c.ClinicId == clinicId.Value);
        else
        {
            var assignedClinicIds = await _users.QueryUserClinicAssignments()
                .Where(a => a.UserId == userId
                    && a.CenterId == centerId
                    && a.IsActive
                    && a.RevokedAt == null)
                .Select(a => a.ClinicId)
                .ToListAsync(cancellationToken);

            query = query.Where(c => assignedClinicIds.Contains(c.ClinicId));
        }

        var rows = await query
            .OrderByDescending(c => c.RequestedAt)
            .ToListAsync(cancellationToken);

        var result = new List<ConsultationDto>(rows.Count);
        foreach (var row in rows)
            result.Add(await MapAsync(row, cancellationToken));

        return result;
    }

    #region Private Helpers

    private async Task EnsureDoctorClinicAsync(
        Guid centerId,
        Guid clinicId,
        string userId,
        CancellationToken cancellationToken)
    {
        await DoctorClinicAccess.RequireActiveAssignmentAsync(
            _users, _centerClinics, centerId, clinicId, userId, cancellationToken);
    }

    private async Task EnsureHasAnyActiveClinicAssignmentAsync(
        Guid centerId,
        string userId,
        CancellationToken cancellationToken)
    {
        var assigned = await _users.QueryUserClinicAssignments()
            .AnyAsync(a => a.UserId == userId
                && a.CenterId == centerId
                && a.IsActive
                && a.RevokedAt == null, cancellationToken);

        if (!assigned)
            throw new DoctorClinicNotAssignedException();
    }

    private async Task EnsureMemberInCenterAsync(
        Guid memberId,
        Guid centerId,
        CancellationToken cancellationToken)
    {
        var member = await _members.GetByIdInCenterAsync(
            memberId,
            centerId,
            asNoTracking: true,
            cancellationToken);

        if (member is null)
            throw new MemberNotFoundException();
    }

    private async Task EnsureActiveConsultationTypeAsync(
        short consultationTypeId,
        CancellationToken cancellationToken)
    {
        var type = await _referenceData.ConsultationTypes(asNoTracking: true)
            .FirstOrDefaultAsync(t => t.ConsultationTypeId == consultationTypeId, cancellationToken);

        if (type is null || !type.IsActive)
            throw new InvalidConsultationDataException("نوع الاستشارة غير موجود أو غير فعّال.");
    }

    private async Task EnsureConsultantUserAsync(
        string? consultantUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(consultantUserId))
            return;

        var user = await _users.GetByIdAsync(consultantUserId, asNoTracking: true, cancellationToken);
        if (user is null || !user.IsActive)
            throw new InvalidConsultationDataException("الطبيب الاستشاري غير موجود أو غير فعّال.");
    }

    private async Task<ConsultationDto> MapAsync(
        Consultation consultation,
        CancellationToken cancellationToken)
    {
        var clinicName = await _referenceData.Clinics(asNoTracking: true)
            .Where(c => c.ClinicId == consultation.ClinicId)
            .Select(c => c.NameAr)
            .FirstOrDefaultAsync(cancellationToken);

        var typeName = await _referenceData.ConsultationTypes(asNoTracking: true)
            .Where(t => t.ConsultationTypeId == consultation.ConsultationTypeId)
            .Select(t => t.NameAr)
            .FirstOrDefaultAsync(cancellationToken);

        var userIds = new List<string> { consultation.RequestedByUserId };
        if (!string.IsNullOrEmpty(consultation.ConsultantUserId))
            userIds.Add(consultation.ConsultantUserId);

        var names = await _users.GetDisplayNamesByIdsAsync(userIds, cancellationToken);
        names.TryGetValue(consultation.RequestedByUserId, out var requestedByName);

        return new ConsultationDto
        {
            ConsultationId = consultation.ConsultationId,
            MemberId = consultation.MemberId,
            CenterId = consultation.CenterId,
            ClinicId = consultation.ClinicId,
            ClinicNameAr = clinicName,
            RequestedByUserId = consultation.RequestedByUserId,
            RequestedByUserName = requestedByName,
            ConsultantUserId = consultation.ConsultantUserId,
            ConsultantName = consultation.ConsultantName,
            ConsultationTypeId = consultation.ConsultationTypeId,
            ConsultationTypeNameAr = typeName,
            ReferralReason = consultation.ReferralReason,
            Status = consultation.Status,
            ResultText = consultation.ResultText,
            RequestedAt = consultation.RequestedAt,
            ResultedAt = consultation.ResultedAt
        };
    }

    private string NormalizeRequiredText(string? value, string missingMessage)
    {
        var sanitized = _textSanitizer.SanitizePlainText(value)?.Trim();
        if (string.IsNullOrEmpty(sanitized))
            throw new InvalidConsultationDataException(missingMessage);
        return sanitized;
    }

    private string? NormalizeOptionalText(string? value)
    {
        var sanitized = _textSanitizer.SanitizePlainText(value)?.Trim();
        return string.IsNullOrEmpty(sanitized) ? null : sanitized;
    }

    private static string? NormalizeOptionalUserId(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private void RecordAudit(
        string userId,
        Guid centerId,
        string eventType,
        string result,
        string entityId,
        string reasonText,
        string? oldValues,
        string? newValues)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = eventType,
            Result = result,
            EntityType = nameof(Consultation),
            EntityId = entityId,
            ReasonText = reasonText,
            OldValues = oldValues,
            NewValues = newValues,
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

    #endregion
}
