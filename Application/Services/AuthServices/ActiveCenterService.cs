using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using System.Net;
using System.Text.Json;

namespace Application.Services.AuthServices;

public class ActiveCenterService : IActiveCenterService
{
    private readonly IAccessContextRepository _access;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentCenterService _currentCenter;
    private readonly ITokenService _tokens;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;

    public ActiveCenterService(
        IAccessContextRepository access,
        ICurrentUserService currentUser,
        ICurrentCenterService currentCenter,
        ITokenService tokens,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork)
    {
        _access = access;
        _currentUser = currentUser;
        _currentCenter = currentCenter;
        _tokens = tokens;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<AssignedCenterDto>> GetMyCentersAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        return await _access.GetActiveCentersAsync(userId, cancellationToken);
    }

    public async Task<TokenGenerationResultDto> SelectAsync(
        Guid? centerId,
        string? currentRefreshToken = null,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        var previousCenterId = _currentCenter.IsCentralContext ? null : _currentCenter.CenterId;

        if (centerId is null)
        {
            var snapshot = await _access.LoadAsync(userId, cancellationToken);
            if (!CenterPermissionEvaluator.HasCentralAuthority(snapshot.Roles, snapshot.Directs))
            {
                await RecordAsync(userId, previousCenterId, null, succeeded: false, cancellationToken);
                throw new ForbiddenPermissionException("لا توجد صلاحية مركزية تسمح بالعمل دون مركز.");
            }
        }
        else if (!await _access.IsActivelyAssignedAsync(userId, centerId.Value, cancellationToken))
        {
            await RecordAsync(userId, previousCenterId, centerId, succeeded: false, cancellationToken);
            throw new ForbiddenPermissionException("المستخدم غير مسند إلى هذا المركز أو أن الإسناد غير فعال.");
        }

        var tokens = await _tokens.GenerateTokensAsync(userId, centerId);
        await RecordAsync(userId, previousCenterId, centerId, succeeded: true, cancellationToken);

        if (!string.IsNullOrWhiteSpace(currentRefreshToken))
            await _tokens.RevokeRefreshTokenAsync(currentRefreshToken);

        return tokens;
    }

    public async Task<Guid> RequireOperationalCenterAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        if (_currentCenter.IsCentralContext || _currentCenter.CenterId is not Guid claimed)
            throw new InvalidUserDataException("العملية المحلية تتطلب مركزاً نشطاً.");

        var usable = ActiveCenterSessionGate.ResolveClaimedCenter(
            claimed,
            await _access.IsActivelyAssignedAsync(userId, claimed, cancellationToken));

        if (usable is not Guid centerId)
            throw new ForbiddenPermissionException("المستخدم غير مسند إلى المركز النشط أو أن الإسناد لم يعد فعالاً.");

        return centerId;
    }

    private async Task RecordAsync(
        string userId,
        Guid? previousCenterId,
        Guid? requestedCenterId,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var auditCenterId = succeeded ? requestedCenterId : previousCenterId;
        var hasCenter = auditCenterId.HasValue;
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = hasCenter ? CenterClaimTypes.Center : CenterClaimTypes.Central,
            CenterId = hasCenter ? auditCenterId : null,
            EventType = "CenterChange",
            Result = succeeded ? "SUCCESS" : "FAILURE",
            EntityType = "ActiveCenter",
            EntityId = requestedCenterId?.ToString(),
            ReasonText = succeeded ? "تم تغيير المركز النشط" : "رُفض تغيير المركز النشط",
            OldValues = JsonSerializer.Serialize(new Dictionary<string, string?> { ["centerId"] = previousCenterId?.ToString() }),
            NewValues = JsonSerializer.Serialize(new Dictionary<string, string?> { ["centerId"] = requestedCenterId?.ToString() }),
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private string RequireUser()
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId) || _currentUser.UserId == "0")
            throw new UnauthenticatedUserException();
        return _currentUser.UserId;
    }
}
