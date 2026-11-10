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
/// إنشاء ومتابعة الاستقصاءات المخبرية والشعاعية مع ربط اختياري بإجراء مؤجّل دون إغلاق تلقائي (FR-047).
/// </summary>
public class InvestigationService : IInvestigationService
{
    public const string InvestigationCreatedEventType = "InvestigationCreated";
    public const string InvestigationResultUpdatedEventType = "InvestigationResultUpdated";
    public const string StatusPending = "PENDING";
    public const string StatusCompleted = "COMPLETED";
    public const string DeferredStatusRequested = ClinicWorkspaceService.DeferredStatusRequested;

    private readonly IInvestigationRepository _investigations;
    private readonly IClinicWorkspaceRepository _clinicWorkspace;
    private readonly IMemberRepository _members;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IUserRepository _users;
    private readonly ICenterClinicRepository _centerClinics;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;
    private readonly ITextInputSanitizer _textSanitizer;

    public InvestigationService(
        IInvestigationRepository investigations,
        IClinicWorkspaceRepository clinicWorkspace,
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
        _investigations = investigations;
        _clinicWorkspace = clinicWorkspace;
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
    public async Task<InvestigationDto> CreateAsync(
        Guid memberId,
        CreateInvestigationRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new InvalidInvestigationDataException("بيانات الاستقصاء مطلوبة.");

        if (memberId == Guid.Empty)
            throw new InvalidInvestigationDataException("معرّف المنتسب مطلوب.");

        if (request.ClinicId == Guid.Empty)
            throw new InvalidInvestigationDataException("معرّف العيادة مطلوب.");

        if (request.InvestigationTypeId <= 0)
            throw new InvalidInvestigationDataException("نوع الاستقصاء مطلوب.");

        var testName = NormalizeRequiredText(request.TestName, "اسم الفحص أو الاختبار مطلوب.");
        var requestReason = NormalizeRequiredText(request.RequestReason, "سبب الطلب مطلوب.");
        var resultText = NormalizeOptionalText(request.ResultText);

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        await EnsureDoctorClinicAsync(centerId, request.ClinicId, userId, cancellationToken);
        await EnsureMemberInCenterAsync(memberId, centerId, cancellationToken);
        await EnsureActiveInvestigationTypeAsync(request.InvestigationTypeId, cancellationToken);

        Guid? deferredActionId = null;
        if (request.DeferredActionId.HasValue)
        {
            deferredActionId = await EnsureLinkableDeferredActionAsync(
                request.DeferredActionId.Value,
                memberId,
                centerId,
                cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var hasResult = !string.IsNullOrEmpty(resultText);
        var investigation = new Investigation
        {
            InvestigationId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = request.ClinicId,
            RequestedByUserId = userId,
            InvestigationTypeId = request.InvestigationTypeId,
            TestName = testName,
            RequestReason = requestReason,
            Status = hasResult ? StatusCompleted : StatusPending,
            ResultText = hasResult ? resultText : null,
            RequestedAt = now,
            ResultedAt = hasResult ? now : null,
            DeferredActionId = deferredActionId
        };

        _investigations.Add(investigation);

        RecordAudit(
            userId,
            centerId,
            InvestigationCreatedEventType,
            "SUCCESS",
            investigation.InvestigationId.ToString(),
            $"إنشاء استقصاء للمنتسب {memberId:D} في العيادة {request.ClinicId:D}",
            oldValues: null,
            newValues: JsonSerializer.Serialize(new
            {
                investigation.InvestigationId,
                investigation.MemberId,
                investigation.CenterId,
                investigation.ClinicId,
                investigation.InvestigationTypeId,
                investigation.TestName,
                investigation.Status,
                investigation.RequestedByUserId,
                investigation.DeferredActionId,
                HasResult = hasResult
            }));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await MapAsync(investigation, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<InvestigationDto> UpdateResultAsync(
        Guid investigationId,
        UpdateInvestigationResultRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new InvalidInvestigationDataException("بيانات نتيجة الاستقصاء مطلوبة.");

        if (investigationId == Guid.Empty)
            throw new InvalidInvestigationDataException("معرّف الاستقصاء مطلوب.");

        var resultText = NormalizeRequiredText(request.ResultText, "نتيجة الاستقصاء مطلوبة.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        var investigation = await _investigations.GetByIdInCenterAsync(
            investigationId,
            centerId,
            asNoTracking: false,
            cancellationToken) ?? throw new InvestigationNotFoundException();

        await EnsureDoctorClinicAsync(centerId, investigation.ClinicId, userId, cancellationToken);

        if (!string.Equals(investigation.Status, StatusPending, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidInvestigationStateException(
                string.Equals(investigation.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase)
                    ? "لا يمكن إعادة تسجيل نتيجة لاستقصاء مكتمل."
                    : "لا يمكن تسجيل نتيجة لاستقصاء ملغى أو غير معلّق.");
        }

        var linkedDeferredStatus = investigation.DeferredActionId.HasValue
            ? await _clinicWorkspace.QueryDeferredActions(asNoTracking: true)
                .Where(d => d.DeferredActionId == investigation.DeferredActionId.Value)
                .Select(d => d.Status)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var oldValues = JsonSerializer.Serialize(new
        {
            investigation.Status,
            investigation.ResultText,
            investigation.ResultedAt,
            investigation.DeferredActionId,
            DeferredActionStatus = linkedDeferredStatus
        });

        var now = DateTimeOffset.UtcNow;
        investigation.ResultText = resultText;
        investigation.ResultedAt = now;
        investigation.Status = StatusCompleted;

        // لا يُستدعى CompleteDeferredActionAsync هنا؛ الإجراء المؤجّل يبقى كما هو.
        RecordAudit(
            userId,
            centerId,
            InvestigationResultUpdatedEventType,
            "SUCCESS",
            investigation.InvestigationId.ToString(),
            $"تسجيل نتيجة استقصاء للمنتسب {investigation.MemberId:D}",
            oldValues,
            JsonSerializer.Serialize(new
            {
                investigation.Status,
                investigation.ResultText,
                investigation.ResultedAt,
                investigation.DeferredActionId,
                DeferredActionStatus = linkedDeferredStatus,
                UpdatedByUserId = userId
            }));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await MapAsync(investigation, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<InvestigationDto> GetByIdAsync(
        Guid investigationId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (investigationId == Guid.Empty)
            throw new InvalidInvestigationDataException("معرّف الاستقصاء مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        var investigation = await _investigations.GetByIdInCenterAsync(
            investigationId,
            centerId,
            asNoTracking: true,
            cancellationToken) ?? throw new InvestigationNotFoundException();

        await EnsureDoctorClinicAsync(centerId, investigation.ClinicId, userId, cancellationToken);
        return await MapAsync(investigation, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InvestigationDto>> ListByMemberAsync(
        Guid memberId,
        Guid? clinicId = null,
        string? status = null,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (memberId == Guid.Empty)
            throw new InvalidInvestigationDataException("معرّف المنتسب مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        await EnsureMemberInCenterAsync(memberId, centerId, cancellationToken);

        if (clinicId.HasValue)
        {
            if (clinicId.Value == Guid.Empty)
                throw new InvalidInvestigationDataException("معرّف العيادة غير صالح.");

            await EnsureDoctorClinicAsync(centerId, clinicId.Value, userId, cancellationToken);
        }
        else
        {
            await EnsureHasAnyActiveClinicAssignmentAsync(centerId, userId, cancellationToken);
        }

        var normalizedStatus = NormalizeOptionalStatus(status);

        var query = _investigations.Query(asNoTracking: true)
            .Where(i => i.MemberId == memberId && i.CenterId == centerId);

        if (clinicId.HasValue)
            query = query.Where(i => i.ClinicId == clinicId.Value);
        else
        {
            var assignedClinicIds = await _users.QueryUserClinicAssignments()
                .Where(a => a.UserId == userId
                    && a.CenterId == centerId
                    && a.IsActive
                    && a.RevokedAt == null)
                .Select(a => a.ClinicId)
                .ToListAsync(cancellationToken);

            query = query.Where(i => assignedClinicIds.Contains(i.ClinicId));
        }

        if (normalizedStatus is not null)
            query = query.Where(i => i.Status == normalizedStatus);

        var rows = await query
            .OrderByDescending(i => i.RequestedAt)
            .ToListAsync(cancellationToken);

        var result = new List<InvestigationDto>(rows.Count);
        foreach (var row in rows)
            result.Add(await MapAsync(row, cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InvestigationDto>> ListPendingAsync(
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (clinicId == Guid.Empty)
            throw new InvalidInvestigationDataException("معرّف العيادة مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        await EnsureDoctorClinicAsync(centerId, clinicId, userId, cancellationToken);

        var rows = await _investigations.Query(asNoTracking: true)
            .Where(i => i.CenterId == centerId
                && i.ClinicId == clinicId
                && i.Status == StatusPending)
            .OrderByDescending(i => i.RequestedAt)
            .ToListAsync(cancellationToken);

        var result = new List<InvestigationDto>(rows.Count);
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

    private async Task EnsureActiveInvestigationTypeAsync(
        short investigationTypeId,
        CancellationToken cancellationToken)
    {
        var type = await _referenceData.InvestigationTypes(asNoTracking: true)
            .FirstOrDefaultAsync(t => t.InvestigationTypeId == investigationTypeId, cancellationToken);

        if (type is null || !type.IsActive)
            throw new InvalidInvestigationDataException("نوع الاستقصاء غير موجود أو غير فعّال.");
    }

    /// <summary>
    /// يتحقق من صلاحية ربط إجراء مؤجّل بنفس المنتسب والمركز دون تغيير حالته.
    /// </summary>
    private async Task<Guid> EnsureLinkableDeferredActionAsync(
        Guid deferredActionId,
        Guid memberId,
        Guid centerId,
        CancellationToken cancellationToken)
    {
        if (deferredActionId == Guid.Empty)
            throw new InvalidInvestigationDataException("معرّف الإجراء المؤجّل غير صالح.");

        var action = await _clinicWorkspace.QueryDeferredActions(asNoTracking: true)
            .FirstOrDefaultAsync(
                d => d.DeferredActionId == deferredActionId && d.CenterId == centerId,
                cancellationToken);

        if (action is null)
            throw new DeferredActionNotFoundException();

        if (action.MemberId != memberId)
            throw new InvalidInvestigationDataException("الإجراء المؤجّل لا يخص هذا المنتسب.");

        if (!string.Equals(action.Status, DeferredStatusRequested, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidInvestigationDataException(
                "لا يمكن ربط الاستقصاء إلا بإجراء مؤجّل بحالة مطلوب.");
        }

        return action.DeferredActionId;
    }

    private async Task<InvestigationDto> MapAsync(
        Investigation investigation,
        CancellationToken cancellationToken)
    {
        var clinicName = await _referenceData.Clinics(asNoTracking: true)
            .Where(c => c.ClinicId == investigation.ClinicId)
            .Select(c => c.NameAr)
            .FirstOrDefaultAsync(cancellationToken);

        var typeName = await _referenceData.InvestigationTypes(asNoTracking: true)
            .Where(t => t.InvestigationTypeId == investigation.InvestigationTypeId)
            .Select(t => t.NameAr)
            .FirstOrDefaultAsync(cancellationToken);

        var names = await _users.GetDisplayNamesByIdsAsync(
            new[] { investigation.RequestedByUserId },
            cancellationToken);
        names.TryGetValue(investigation.RequestedByUserId, out var requestedByName);

        string? deferredStatus = null;
        if (investigation.DeferredActionId.HasValue)
        {
            deferredStatus = await _clinicWorkspace.QueryDeferredActions(asNoTracking: true)
                .Where(d => d.DeferredActionId == investigation.DeferredActionId.Value)
                .Select(d => d.Status)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new InvestigationDto
        {
            InvestigationId = investigation.InvestigationId,
            MemberId = investigation.MemberId,
            CenterId = investigation.CenterId,
            ClinicId = investigation.ClinicId,
            ClinicNameAr = clinicName,
            RequestedByUserId = investigation.RequestedByUserId,
            RequestedByUserName = requestedByName,
            InvestigationTypeId = investigation.InvestigationTypeId,
            InvestigationTypeNameAr = typeName,
            TestName = investigation.TestName,
            RequestReason = investigation.RequestReason,
            Status = investigation.Status,
            ResultText = investigation.ResultText,
            RequestedAt = investigation.RequestedAt,
            ResultedAt = investigation.ResultedAt,
            DeferredActionId = investigation.DeferredActionId,
            DeferredActionStatus = deferredStatus
        };
    }

    private string NormalizeRequiredText(string? value, string missingMessage)
    {
        var sanitized = _textSanitizer.SanitizePlainText(value)?.Trim();
        if (string.IsNullOrEmpty(sanitized))
            throw new InvalidInvestigationDataException(missingMessage);
        return sanitized;
    }

    private string? NormalizeOptionalText(string? value)
    {
        var sanitized = _textSanitizer.SanitizePlainText(value)?.Trim();
        return string.IsNullOrEmpty(sanitized) ? null : sanitized;
    }

    private static string? NormalizeOptionalStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        var normalized = status.Trim().ToUpperInvariant();
        if (normalized is not (StatusPending or StatusCompleted or "CANCELLED"))
            throw new InvalidInvestigationDataException("حالة الاستقصاء غير صالحة.");

        return normalized;
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
            EntityType = nameof(Investigation),
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
