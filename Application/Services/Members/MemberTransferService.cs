using System.Net;
using System.Text.Json;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Domain.Entities.Patient;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Members;

/// <summary>
/// ينفّذ FR-073/FR-074 عبر استدعاء المسار الذري المعتمد <c>patient.transferMember</c>
/// بعد تحققات الصلاحية والأهلية في طبقة التطبيق.
/// </summary>
/// <remarks>
/// سياسات مستمدة من مخطط قاعدة البيانات (InitialDatabaseObjects):
/// - تغيير <c>CurrentMedicalCenterId</c> ممنوع مباشرة ويُمر فقط عبر <c>transferMember</c>.
/// - <c>QueueMedicalCenterId</c> تاريخي ولا يُغيَّر عند النقل.
/// - الملف المصدَّر يُعاد إلى <c>UNEXPORTED</c> داخل الدالة (A6 محسوم بالمخطط).
/// - أهلية A3 المؤقتة في التطبيق: منع مؤجل REQUESTED وقفل تحرير عيادة.
/// </remarks>
public class MemberTransferService : IMemberTransferService
{
    public const string TransferExecutedEventType = "MemberTransferExecuted";
    public const string CrossCenterDataType = "MemberTransferHistory";
    public const string DeferredRequestedStatus = "REQUESTED";
    public const int MaxTransferReasonLength = 1000;

    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;
    private readonly IMemberRepository _members;
    private readonly IMedicalCenterRepository _centers;
    private readonly IClinicWorkspaceRepository _clinicWorkspace;
    private readonly IAuditLogRepository _audit;
    private readonly ICrossCenterAccessService _crossCenterAccess;
    private readonly IUnitOfWork _unitOfWork;

    public MemberTransferService(
        IPermissionGate permissionGate,
        ICurrentUserService currentUser,
        IMemberRepository members,
        IMedicalCenterRepository centers,
        IClinicWorkspaceRepository clinicWorkspace,
        IAuditLogRepository audit,
        ICrossCenterAccessService crossCenterAccess,
        IUnitOfWork unitOfWork)
    {
        _permissionGate = permissionGate;
        _currentUser = currentUser;
        _members = members;
        _centers = centers;
        _clinicWorkspace = clinicWorkspace;
        _audit = audit;
        _crossCenterAccess = crossCenterAccess;
        _unitOfWork = unitOfWork;
    }

    #region Public Operations

    /// <inheritdoc />
    public async Task<MemberTransferResultDto> TransferLocalAsync(
        Guid memberId,
        TransferMemberRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var sourceCenterId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.Transfer,
            clientCenterId,
            cancellationToken);

        return await ExecuteTransferAsync(
            memberId,
            request,
            requiredSourceCenterId: sourceCenterId,
            auditCenterId: sourceCenterId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MemberTransferResultDto> TransferCentralAsync(
        Guid memberId,
        TransferMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        await _permissionGate.EnsureCentralAsync(
            MilitaryHealthPermissions.Members.Transfer,
            cancellationToken);

        return await ExecuteTransferAsync(
            memberId,
            request,
            requiredSourceCenterId: null,
            auditCenterId: null,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MemberTransferHistoryDto> GetHistoryLocalAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.View,
            clientCenterId,
            cancellationToken);

        var member = await _members.GetByIdInCenterAsync(memberId, centerId, asNoTracking: true, cancellationToken)
            ?? throw new MemberNotFoundException();

        return await BuildHistoryAsync(member, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MemberTransferHistoryDto> GetHistoryCentralAsync(
        Guid memberId,
        string accessReason,
        CancellationToken cancellationToken = default)
    {
        await _permissionGate.EnsureCentralAsync(
            MilitaryHealthPermissions.Members.ViewCentral,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(accessReason))
            throw new InvalidMemberTransferDataException("سبب الاطلاع العابر مطلوب.");

        var member = await _members.GetByIdAsync(memberId, asNoTracking: true, cancellationToken)
            ?? throw new MemberNotFoundException();

        var history = await BuildHistoryAsync(member, cancellationToken);

        await _crossCenterAccess.RecordAsync(
            member.CurrentMedicalCenterId,
            accessReason.Trim(),
            CrossCenterDataType,
            queryCriteria: JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["memberId"] = memberId.ToString("D")
            }),
            entityType: nameof(Member),
            entityId: memberId.ToString(),
            cancellationToken);

        return history;
    }

    #endregion

    #region Transfer Core

    private async Task<MemberTransferResultDto> ExecuteTransferAsync(
        Guid memberId,
        TransferMemberRequest request,
        Guid? requiredSourceCenterId,
        Guid? auditCenterId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reason = NormalizeReason(request.TransferReason);
        if (request.ToCenterId == Guid.Empty)
            throw new InvalidMemberTransferDataException("المركز الهدف مطلوب.");

        var userId = RequireUser();

        Member? memberLookup = requiredSourceCenterId is Guid source
            ? await _members.GetByIdInCenterAsync(memberId, source, asNoTracking: true, cancellationToken)
            : await _members.GetByIdAsync(memberId, asNoTracking: true, cancellationToken);

        if (memberLookup is null)
            throw new MemberNotFoundException();

        var fromCenterId = memberLookup.CurrentMedicalCenterId;
        if (request.ToCenterId == fromCenterId)
            throw new MemberTransferSameCenterException();

        if (request.ExpectedDataRevision is long expected && memberLookup.DataRevision != expected)
        {
            throw new MemberTransferConflictException(
                "تعارض مراجعة بيانات الملف. أعد تحميل الملف ثم نفّذ النقل مجدداً.");
        }

        var toCenter = await _centers.GetByIdAsync(request.ToCenterId, asNoTracking: true, cancellationToken);
        MedicalCenterActivityGuard.EnsureAcceptsNewRelations(toCenter);

        var fromCenter = await _centers.GetByIdAsync(fromCenterId, asNoTracking: true, cancellationToken)
            ?? throw new MedicalCenterNotFoundException();

        await EnsureEligibleForTransferAsync(memberId, cancellationToken);

        var notices = new List<string>();
        var wasExported = string.Equals(memberLookup.ExportStatus, "EXPORTED", StringComparison.OrdinalIgnoreCase);
        if (wasExported)
        {
            notices.Add(
                "أُعيدت حالة التصدير إلى غير مصدَّر وفق دالة النقل المعتمدة في قاعدة البيانات (patient.transferMember).");
        }

        notices.Add(
            "رقم الدور ومركز أصله التاريخي يبقيان كما سُجّلا عند الاستقبال؛ لا يُعاد تخصيص الدور عند النقل.");

        var now = DateTimeOffset.UtcNow;
        var centerMemberId = memberLookup.FileNumber.ToString();
        var preservedFileNumber = memberLookup.FileNumber;
        var exportBefore = memberLookup.ExportStatus;

        await using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var transferId = await _members.ExecuteAuthorizedTransferAsync(
                memberId,
                request.ToCenterId,
                centerMemberId,
                userId,
                reason,
                now,
                cancellationToken);

            // Application-level audit complements the DB function audit (eventType TRANSFER).
            RecordAudit(
                userId,
                auditCenterId ?? fromCenterId,
                TransferExecutedEventType,
                memberId.ToString(),
                "تم نقل ملف المنتسب بين المراكز",
                oldValues: JsonSerializer.Serialize(new
                {
                    CurrentMedicalCenterId = fromCenterId,
                    ExportStatus = exportBefore,
                    DataRevision = memberLookup.DataRevision,
                    FileNumber = preservedFileNumber
                }),
                newValues: JsonSerializer.Serialize(new
                {
                    TransferId = transferId,
                    ToCenterId = request.ToCenterId,
                    TransferReason = reason,
                    FileNumber = preservedFileNumber,
                    ExportStatusReset = wasExported
                }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            var updated = await _members.GetByIdAsync(memberId, asNoTracking: true, cancellationToken)
                ?? throw new MemberNotFoundException();

            return new MemberTransferResultDto
            {
                TransferId = transferId,
                MemberId = memberId,
                FileNumber = preservedFileNumber,
                FromCenterId = fromCenterId,
                FromCenterNameAr = fromCenter.NameAr,
                ToCenterId = request.ToCenterId,
                ToCenterNameAr = toCenter!.NameAr,
                TransferReason = reason,
                ExecutedByUserId = userId,
                ExecutedAt = now,
                QueueDate = updated.QueueDate,
                QueueNumber = updated.QueueNumber,
                DataRevision = updated.DataRevision,
                ExportStatusAtTransfer = exportBefore,
                NoticesAr = notices
            };
        }
        catch (DbUpdateException ex)
        {
            await tx.RollbackAsync(cancellationToken);
            throw MapDatabaseFailure(ex);
        }
        catch (Exception ex) when (IsPostgresException(ex))
        {
            await tx.RollbackAsync(cancellationToken);
            throw MapPostgresFailureMessage(ex.Message);
        }
        catch (BusinessException)
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    #endregion

    #region Validation / History Helpers

    /// <summary>
    /// سياسة أهلية مؤقتة في التطبيق: يمنع النقل عند قفل تحرير نشط أو إجراء مؤجل REQUESTED.
    /// </summary>
    private async Task EnsureEligibleForTransferAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var hasOpenDeferred = await _clinicWorkspace.QueryDeferredActions()
            .AnyAsync(
                d => d.MemberId == memberId
                     && d.Status == DeferredRequestedStatus,
                cancellationToken);

        if (hasOpenDeferred)
        {
            throw new MemberTransferNotEligibleException(
                "لا يمكن نقل الملف لوجود إجراء مؤجل غير منجز. أكمِل أو أغلق الإجراء ثم أعد المحاولة (سياسة مؤقتة لـ A3).");
        }

        var hasEditLock = await _clinicWorkspace.QueryEditLocks()
            .AnyAsync(l => l.MemberId == memberId, cancellationToken);

        if (hasEditLock)
        {
            throw new MemberTransferNotEligibleException(
                "لا يمكن نقل الملف أثناء وجود قفل تحرير عيادة نشط. أغلِق جلسة الفحص ثم أعد المحاولة (سياسة مؤقتة لـ A3).");
        }
    }

    private async Task<MemberTransferHistoryDto> BuildHistoryAsync(
        Member member,
        CancellationToken cancellationToken)
    {
        var items = await _members.QueryTransfers()
            .Where(t => t.MemberId == member.MemberId)
            .OrderBy(t => t.ExecutedAt)
            .ThenBy(t => t.TransferId)
            .Select(t => new MemberTransferHistoryItemDto
            {
                TransferId = t.TransferId,
                FromCenterId = t.FromCenterId,
                FromCenterNameAr = t.FromCenter.NameAr,
                ToCenterId = t.ToCenterId,
                ToCenterNameAr = t.ToCenter.NameAr,
                TransferReason = t.TransferReason,
                ExecutedByUserId = t.ExecutedByUserId,
                ExecutedByUserName = t.ExecutedByUser.UserName,
                ExecutedAt = t.ExecutedAt
            })
            .ToListAsync(cancellationToken);

        var hasTransfers = items.Count > 0;
        return new MemberTransferHistoryDto
        {
            MemberId = member.MemberId,
            FileNumber = member.FileNumber,
            CurrentMedicalCenterId = member.CurrentMedicalCenterId,
            CurrentMedicalCenterNameAr = member.CurrentMedicalCenter?.NameAr,
            HasTransfers = hasTransfers,
            EmptyMessageAr = hasTransfers
                ? null
                : "لم يُنقل هذا الملف بين المراكز؛ المركز الحالي هو مركزه الأصلي ضمن النظام.",
            Items = items
        };
    }

    private static string NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidMemberTransferDataException("سبب النقل إلزامي.");

        var trimmed = reason.Trim();
        if (trimmed.Length > MaxTransferReasonLength)
        {
            throw new InvalidMemberTransferDataException(
                $"سبب النقل يتجاوز الحد الأقصى ({MaxTransferReasonLength} حرفاً).");
        }

        return trimmed;
    }

    private static bool IsPostgresException(Exception ex)
        => ex.GetType().FullName == "Npgsql.PostgresException"
           || ex.InnerException?.GetType().FullName == "Npgsql.PostgresException";

    private static BusinessException MapDatabaseFailure(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return MapPostgresFailureMessage(message);
    }

    private static BusinessException MapPostgresFailureMessage(string message)
    {
        if (message.Contains("same medical center", StringComparison.OrdinalIgnoreCase))
            return new MemberTransferSameCenterException();
        if (message.Contains("transferReason is required", StringComparison.OrdinalIgnoreCase))
            return new InvalidMemberTransferDataException("سبب النقل إلزامي.");
        if (message.Contains("Destination medical center", StringComparison.OrdinalIgnoreCase))
            return new InactiveMedicalCenterException();
        if (message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            return new MemberNotFoundException();
        if (message.Contains("memberCenterIdentity", StringComparison.OrdinalIgnoreCase))
            return new MemberTransferConflictException("لا توجد هوية مركز مفتوحة صالحة للنقل.");

        return new MemberTransferConflictException(
            "تعذر إتمام النقل بسبب قيد قاعدة البيانات. أعد المحاولة أو راجع حالة الملف.");
    }

    private string RequireUser()
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId) || userId == "0")
            throw new UnauthenticatedUserException();
        return userId;
    }

    private void RecordAudit(
        string userId,
        Guid centerId,
        string eventType,
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
            Result = "SUCCESS",
            EntityType = nameof(Member),
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

    #endregion
}
