using System.Net;
using System.Text.Json;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.Centers;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Domain.Entities.Core;

namespace Application.Services.Centers;

/// <summary>
/// ينفّذ FR-012: جرد الأيتام، إسنادها في معاملة واحدة، ثم إثبات التحقق.
/// الإسناد وحده لا يفتح تعدد المراكز؛ القفل يقرأ سجل التحقق فقط.
/// </summary>
public class HistoricalCenterMigrationService : IHistoricalCenterMigrationService
{
    public const string ExecutedEventType = "HistoricalCenterMigrationExecuted";
    public const string VerifiedEventType = "HistoricalCenterMigrationVerified";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IHistoricalCenterMigrationRepository _store;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogRepository _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionGate _permissionGate;

    public HistoricalCenterMigrationService(
        IHistoricalCenterMigrationRepository store,
        IUnitOfWork unitOfWork,
        IAuditLogRepository audit,
        ICurrentUserService currentUser,
        IPermissionGate permissionGate)
    {
        _store = store;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _currentUser = currentUser;
        _permissionGate = permissionGate;
    }

    public async Task<HistoricalMigrationReportDto> PreviewAsync(CancellationToken cancellationToken = default)
    {
        EnsurePermission();
        var scan = await _store.ScanAsync(cancellationToken);
        var verified = await _store.GetLatestCompletionAsync(cancellationToken) is not null;
        return ToReport(scan, targetCenterId: null, verified);
    }

    public async Task<HistoricalMigrationReportDto> ExecuteAsync(
        Guid targetCenterId,
        CancellationToken cancellationToken = default)
    {
        EnsurePermission();
        await EnsureActiveTargetAsync(targetCenterId, cancellationToken);

        var preview = await _store.ScanAsync(cancellationToken);
        if (preview.HasUnassignable)
            throw new HistoricalMigrationBlockedException(ToReport(preview, targetCenterId, verified: false));

        var userId = RequireUser();
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var assigned = await _store.AssignRepairableAsync(targetCenterId, cancellationToken);
            if (assigned.HasUnassignable)
                throw new HistoricalMigrationBlockedException(ToReport(assigned, targetCenterId, verified: false));

            WriteAudit(
                userId,
                ExecutedEventType,
                targetCenterId,
                "تم إسناد السجلات التشغيلية المحلية اليتيمة إلى المركز المعتمد دون إغلاق التحقق.",
                new { targetCenterId, assigned = assigned.TotalAssigned, entities = assigned.Slices.Select(s => new { s.EntityName, s.AssignedCount }) });

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToReport(assigned, targetCenterId, verified: false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            _store.DiscardPendingChanges();
            throw;
        }
    }

    public async Task<HistoricalMigrationStatusDto> VerifyAsync(
        Guid? targetCenterId,
        CancellationToken cancellationToken = default)
    {
        EnsurePermission();
        var existing = await _store.GetLatestCompletionAsync(cancellationToken);
        if (existing is not null)
            return await BuildStatusAsync(existing, cancellationToken);

        var scan = await _store.ScanAsync(cancellationToken);
        if (scan.TotalOrphans > 0 || scan.HasUnassignable)
            throw new HistoricalMigrationVerificationFailedException(ToReport(scan, targetCenterId, verified: false));

        var target = await ResolveVerificationTargetAsync(targetCenterId, cancellationToken);
        var userId = RequireUser();
        var activeCount = await _store.CountActiveCentersAsync(cancellationToken);
        var completion = new HistoricalCenterMigrationCompletion
        {
            Id = Guid.NewGuid(),
            TargetCenterId = target,
            VerifiedAt = DateTimeOffset.UtcNow,
            VerifiedByUserId = userId,
            SummaryJson = JsonSerializer.Serialize(
                scan.Slices.Select(s => new { s.EntityName, s.OrphanCount }),
                JsonOptions),
            ActiveCenterCountAtVerification = activeCount,
            PreExistingMultiCenterDetected = activeCount > 1
        };

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            _store.AddCompletion(completion);
            WriteAudit(
                userId,
                VerifiedEventType,
                target,
                completion.PreExistingMultiCenterDetected
                    ? "اكتمل التحقق مع وجود أكثر من مركز نشط مسبقاً. لم يُعطَّل أي مركز."
                    : "اكتمل التحقق من ترحيل السجلات التاريخية.",
                new
                {
                    completion.Id,
                    completion.TargetCenterId,
                    completion.ActiveCenterCountAtVerification,
                    completion.PreExistingMultiCenterDetected
                });
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            _store.DiscardPendingChanges();
            throw;
        }

        return await BuildStatusAsync(completion, cancellationToken);
    }

    public async Task<HistoricalMigrationStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        EnsurePermission();
        var completion = await _store.GetLatestCompletionAsync(cancellationToken);
        return await BuildStatusAsync(completion, cancellationToken);
    }

    public async Task EnsureAdditionalActiveCenterAllowedAsync(CancellationToken cancellationToken = default)
    {
        EnsurePermission();
        if (await _store.GetLatestCompletionAsync(cancellationToken) is not null)
            return;

        var active = await _store.CountActiveCentersAsync(cancellationToken);
        if (active >= 1)
            throw new MultiCenterActivationBlockedException();
    }

    private async Task EnsureActiveTargetAsync(Guid targetCenterId, CancellationToken cancellationToken)
    {
        if (targetCenterId == Guid.Empty || !await _store.IsActiveCenterAsync(targetCenterId, cancellationToken))
            throw new HistoricalMigrationTargetInvalidException("مركز الترحيل المعتمد غير موجود أو غير فعّال.");
    }

    private async Task<Guid> ResolveVerificationTargetAsync(Guid? targetCenterId, CancellationToken cancellationToken)
    {
        if (targetCenterId is Guid requested && requested != Guid.Empty)
        {
            await EnsureActiveTargetAsync(requested, cancellationToken);
            return requested;
        }

        var sole = await _store.GetSoleActiveCenterIdAsync(cancellationToken);
        if (sole is Guid centerId)
            return centerId;

        throw new HistoricalMigrationTargetInvalidException(
            "يجب تحديد مركز الترحيل المعتمد عند التحقق إذا لم يوجد مركز نشط واحد فقط.");
    }

    private async Task<HistoricalMigrationStatusDto> BuildStatusAsync(
        HistoricalCenterMigrationCompletion? completion,
        CancellationToken cancellationToken)
    {
        var active = await _store.CountActiveCentersAsync(cancellationToken);
        var verified = completion is not null;
        return new HistoricalMigrationStatusDto
        {
            MigrationVerified = verified,
            ActiveCenterCount = active,
            PreExistingMultiCenterWithoutVerification = active > 1 && !verified,
            Completion = completion is null ? null : ToCompletion(completion)
        };
    }

    private void EnsurePermission()
        => _permissionGate.EnsureAll(MilitaryHealthPermissions.Centers.Manage);

    private string RequireUser()
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId) || userId == "0")
            throw new UnauthenticatedUserException();
        return userId;
    }

    private void WriteAudit(string userId, string eventType, Guid targetCenterId, string reason, object payload)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Central,
            CenterId = null,
            EventType = eventType,
            Result = "SUCCESS",
            EntityType = nameof(HistoricalCenterMigrationCompletion),
            EntityId = targetCenterId.ToString(),
            ReasonText = reason,
            NewValues = JsonSerializer.Serialize(payload, JsonOptions),
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });
    }

    private static HistoricalMigrationReportDto ToReport(
        HistoricalMigrationScan scan,
        Guid? targetCenterId,
        bool verified)
        => new()
        {
            TargetCenterId = targetCenterId,
            TotalOrphanCount = scan.TotalOrphans,
            TotalUnassignableCount = scan.TotalUnassignable,
            TotalAssignedCount = scan.TotalAssigned,
            MigrationVerified = verified,
            Entities = scan.Slices.Select(s => new HistoricalMigrationEntityCountDto
            {
                EntityName = s.EntityName,
                LabelAr = s.LabelAr,
                OrphanCount = s.OrphanCount,
                AssignedCount = s.AssignedCount,
                Unassignable = s.Unassignable,
                BlockReason = s.BlockReason,
                SampleIds = s.SampleIds
            }).ToList()
        };

    private static HistoricalMigrationCompletionDto ToCompletion(HistoricalCenterMigrationCompletion completion)
        => new()
        {
            Id = completion.Id,
            TargetCenterId = completion.TargetCenterId,
            VerifiedAt = completion.VerifiedAt,
            VerifiedByUserId = completion.VerifiedByUserId,
            SummaryJson = completion.SummaryJson,
            ActiveCenterCountAtVerification = completion.ActiveCenterCountAtVerification,
            PreExistingMultiCenterDetected = completion.PreExistingMultiCenterDetected
        };
}
