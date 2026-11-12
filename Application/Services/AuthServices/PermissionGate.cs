using Application.Common.Security;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Application.Services.AuthServices;

public sealed class PermissionGate : IPermissionGate
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserPermissionService _permissions;
    private readonly IAccessContextRepository _access;
    private readonly ICurrentCenterService _currentCenter;
    private readonly ILogger<PermissionGate> _logger;

    public PermissionGate(
        IHttpContextAccessor httpContextAccessor,
        IUserPermissionService permissions,
        IAccessContextRepository access,
        ICurrentCenterService currentCenter,
        ILogger<PermissionGate> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _permissions = permissions;
        _access = access;
        _currentCenter = currentCenter;
        _logger = logger;
    }

    public IReadOnlyCollection<string> GetPermissions()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return Array.Empty<string>();

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return Array.Empty<string>();

        return _permissions.GetPermissionNames(userId);
    }

    public bool Has(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission)) return false;
        return GetPermissions().Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasAll(params string[] permissions)
    {
        if (permissions == null || permissions.Length == 0) return true;
        var owned = GetPermissions();
        return permissions.All(p =>
            !string.IsNullOrWhiteSpace(p) &&
            owned.Contains(p, StringComparer.OrdinalIgnoreCase));
    }

    public void EnsureAll(params string[] permissions)
    {
        var userId = RequireUser();
        if (HasAll(permissions)) return;

        var required = string.Join(",", permissions.Where(p => !string.IsNullOrWhiteSpace(p)));
        throw Denied(userId, required, "permission-missing", "PermissionGate.EnsureAll");
    }

    public async Task<Guid> EnsureLocalAsync(
        string permission,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        var sessionCenter = _currentCenter.CenterId;
        if (_currentCenter.IsCentralContext || sessionCenter is null)
            throw Denied(userId, permission, "no-active-center");

        var claimed = sessionCenter.Value;
        var active = ActiveCenterSessionGate.ResolveClaimedCenter(
            claimed,
            await _access.IsActivelyAssignedAsync(userId, claimed, cancellationToken));

        if (active is null)
            throw Denied(userId, permission, "assignment-inactive");

        var centerId = active.Value;
        if (clientCenterId is Guid requested && requested != centerId)
            throw Denied(userId, permission, "client-center-mismatch");

        if (!await HasEffectivePermissionAsync(userId, permission, centerId, cancellationToken))
            throw Denied(userId, permission, "permission-missing");

        return centerId;
    }

    public async Task EnsureCentralAsync(string permission, CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();

        if (!await HasEffectivePermissionAsync(userId, permission, activeCenterId: null, cancellationToken))
            throw Denied(userId, permission, "permission-missing");
    }

    private async Task<bool> HasEffectivePermissionAsync(
        string userId,
        string permission,
        Guid? activeCenterId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(permission))
            return false;

        var snapshot = await _access.LoadAsync(userId, cancellationToken);
        var names = CenterPermissionEvaluator.Resolve(
            snapshot.Roles,
            snapshot.Directs,
            snapshot.Links,
            activeCenterId);

        return names.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    private string RequireUser()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            throw new UnauthenticatedUserException();

        var userId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(userId) || userId == "0")
            throw new UnauthenticatedUserException();

        return userId;
    }

    private string? CurrentUserId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        return user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user?.FindFirst("sub")?.Value;
    }

    private ForbiddenPermissionException Denied(
        string userId,
        string permission,
        string reason,
        string operation = "PermissionGate")
    {
        _logger.LogWarning(
            "Rejected protected operation for user {UserId}. Permission {Permission}. Reason {Reason}.",
            userId,
            permission,
            reason);

        // FR-076 A2: رفض صلاحية تشغيلية عامة لا يُصنَّف ضمن تدقيق IAM الحساس.
        if (!IamSensitiveAccessDenial.RequiresAuditForPermission(permission))
        {
            return new ForbiddenPermissionException("ليس لديك الصلاحية المطلوبة.");
        }

        return ForbiddenPermissionException.AccessDenied(
            message: "ليس لديك الصلاحية المطلوبة.",
            operation: operation,
            reasonCode: reason,
            permission: permission);
    }
}
