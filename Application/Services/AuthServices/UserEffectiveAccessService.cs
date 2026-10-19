using System.Net;
using System.Text.Json;
using Application.Common.Localization;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.AuthServices;

public sealed class UserEffectiveAccessService : IUserEffectiveAccessService
{
    private readonly IUserRepository _users;
    private readonly IAccessContextRepository _access;
    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentCenterService _currentCenter;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;

    public UserEffectiveAccessService(
        IUserRepository users,
        IAccessContextRepository access,
        IPermissionGate permissionGate,
        ICurrentUserService currentUser,
        ICurrentCenterService currentCenter,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _access = access;
        _permissionGate = permissionGate;
        _currentUser = currentUser;
        _currentCenter = currentCenter;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<UserEffectiveAccessDto> GetEffectiveAccessAsync(
        string userId,
        Guid? centerId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidUserDataException("معرف المستخدم غير صالح.");

        var actorId = RequireActor();
        var isSelf = string.Equals(actorId, userId, StringComparison.Ordinal);

        if (!isSelf)
        {
            _permissionGate.EnsureAll(SystemPermissions.UserManagement.Read);
            await UserManagementScopeGuard.EnsureCanManageTargetAsync(
                _access, _users, actorId, userId, cancellationToken);
        }

        var user = await _users.GetByIdAsync(userId, asNoTracking: true, cancellationToken)
            ?? throw new UserNotFoundException(userId);

        var snapshot = await _access.LoadAsync(user.Id, cancellationToken);
        var allActiveCenters = await _access.GetActiveCentersAsync(user.Id, cancellationToken);
        var actorUnrestricted = await UserManagementScopeGuard.ActorHasUnrestrictedScopeAsync(
            _access, actorId, cancellationToken);

        IReadOnlyList<AssignedCenterDto> viewableCenters = allActiveCenters;
        if (!isSelf && !actorUnrestricted)
        {
            var actorCenters = await _access.GetActiveCentersAsync(actorId, cancellationToken);
            var actorIds = actorCenters.Select(c => c.CenterId).ToHashSet();
            viewableCenters = allActiveCenters
                .Where(c => actorIds.Contains(c.CenterId))
                .ToList();
        }

        var viewableCenterIds = viewableCenters.Select(c => c.CenterId).ToHashSet();
        var contextCenterId = ResolveContextCenterId(
            centerId, isSelf, actorUnrestricted, viewableCenters);

        var roles = await LoadActiveRolesAsync(user.Id, viewableCenterIds, actorUnrestricted || isSelf, cancellationToken);
        var directs = await LoadActiveDirectsAsync(user.Id, viewableCenterIds, actorUnrestricted || isSelf, cancellationToken);

        var effective = MapPermissions(
            CenterPermissionEvaluator.ResolveDetailed(
                snapshot.Roles, snapshot.Directs, snapshot.Links, contextCenterId));

        var byCenter = new List<CenterEffectivePermissionsDto>();
        if (actorUnrestricted || isSelf)
        {
            byCenter.Add(new CenterEffectivePermissionsDto
            {
                CenterId = null,
                IsCentralContext = true,
                Permissions = MapPermissions(
                    CenterPermissionEvaluator.ResolveDetailed(
                        snapshot.Roles, snapshot.Directs, snapshot.Links, null))
            });
        }

        foreach (var center in viewableCenters)
        {
            byCenter.Add(new CenterEffectivePermissionsDto
            {
                CenterId = center.CenterId,
                CenterCode = center.Code,
                CenterNameAr = center.NameAr,
                IsCentralContext = false,
                Permissions = MapPermissions(
                    CenterPermissionEvaluator.ResolveDetailed(
                        snapshot.Roles, snapshot.Directs, snapshot.Links, center.CenterId))
            });
        }

        var dto = new UserEffectiveAccessDto
        {
            UserId = user.Id,
            IsSelfView = isSelf,
            SetupIncomplete = viewableCenters.Count == 0 || roles.Count == 0,
            ContextCenterId = contextCenterId,
            ActiveCenters = viewableCenters.ToList(),
            Roles = roles,
            DirectPermissions = directs,
            EffectivePermissions = effective,
            ByCenter = byCenter
        };

        if (!isSelf)
            await RecordAdminViewAsync(actorId, user.Id, contextCenterId, cancellationToken);

        return dto;
    }

    private Guid? ResolveContextCenterId(
        Guid? requestedCenterId,
        bool isSelf,
        bool actorUnrestricted,
        IReadOnlyList<AssignedCenterDto> viewableCenters)
    {
        var viewableCenterIds = viewableCenters.Select(c => c.CenterId).ToHashSet();

        if (requestedCenterId is Guid requested)
        {
            if (requested == Guid.Empty)
                throw new InvalidUserDataException("معرف المركز غير صالح.");

            if (!viewableCenterIds.Contains(requested))
                throw new ForbiddenPermissionException("لا يمكنك عرض صلاحيات هذا المستخدم خارج نطاق تفويضك.");

            return requested;
        }

        if (isSelf && !_currentCenter.IsCentralContext && _currentCenter.CenterId is Guid sessionCenter)
        {
            if (viewableCenterIds.Contains(sessionCenter))
                return sessionCenter;
        }

        if (!actorUnrestricted && !isSelf && viewableCenters.Count > 0)
            return viewableCenters[0].CenterId;

        return null;
    }

    private async Task<List<EffectiveRoleDto>> LoadActiveRolesAsync(
        string userId,
        HashSet<Guid> viewableCenterIds,
        bool includeCentral,
        CancellationToken cancellationToken)
    {
        var rows = await _users.QueryRoleAssociations()
            .Where(r => r.UserId == userId && r.IsActive && r.Role.IsActive)
            .Include(r => r.Role)
            .Include(r => r.Center)
            .OrderBy(r => r.CenterId == null ? 0 : 1)
            .ThenBy(r => r.Role.Name)
            .ToListAsync(cancellationToken);

        return rows
            .Where(r =>
                (r.CenterId is null && includeCentral)
                || (r.CenterId is Guid cid && viewableCenterIds.Contains(cid)))
            .Select(r => new EffectiveRoleDto
            {
                RoleId = r.RoleId,
                RoleName = r.Role.Name ?? string.Empty,
                RoleNameAr = AuthDisplayNames.RoleAr(r.Role.Name),
                CenterId = r.CenterId,
                CenterCode = r.Center?.Code,
                CenterNameAr = r.Center?.NameAr,
                IsCentralScope = r.CenterId is null
            })
            .ToList();
    }

    private async Task<List<EffectiveDirectPermissionDto>> LoadActiveDirectsAsync(
        string userId,
        HashSet<Guid> viewableCenterIds,
        bool includeCentral,
        CancellationToken cancellationToken)
    {
        var rows = await _users.QueryUserPermissions()
            .Where(p => p.UserId == userId && p.IsActive && p.Permission != null && p.Permission.IsActive)
            .Include(p => p.Permission)
            .Include(p => p.Center)
            .OrderBy(p => p.CenterId == null ? 0 : 1)
            .ThenBy(p => p.Permission!.Name)
            .ToListAsync(cancellationToken);

        return rows
            .Where(p =>
                (p.CenterId is null && includeCentral)
                || (p.CenterId is Guid cid && viewableCenterIds.Contains(cid)))
            .Select(p =>
            {
                var name = p.Permission!.Name ?? string.Empty;
                return new EffectiveDirectPermissionDto
                {
                    PermissionName = name,
                    PermissionNameAr = AuthDisplayNames.PermissionAr(name),
                    CenterId = p.CenterId,
                    CenterCode = p.Center?.Code,
                    CenterNameAr = p.Center?.NameAr,
                    IsCentralScope = p.CenterId is null
                };
            })
            .ToList();
    }

    private static List<EffectivePermissionDto> MapPermissions(
        IReadOnlyList<CenterPermissionEvaluator.EffectivePermission> items)
        => items.Select(p => new EffectivePermissionDto
        {
            Name = p.Name,
            NameAr = AuthDisplayNames.PermissionAr(p.Name),
            FromRole = p.FromRole,
            FromDirect = p.FromDirect,
            Sources = p.Sources.Select(s => new EffectivePermissionSourceDto
            {
                SourceType = s.SourceType,
                RoleId = s.RoleId,
                RoleName = s.RoleName,
                RoleNameAr = string.IsNullOrWhiteSpace(s.RoleName)
                    ? null
                    : AuthDisplayNames.RoleAr(s.RoleName)
            }).ToList()
        }).ToList();

    private async Task RecordAdminViewAsync(
        string actorId,
        string targetUserId,
        Guid? contextCenterId,
        CancellationToken cancellationToken)
    {
        var hasCenter = contextCenterId.HasValue
            || (!_currentCenter.IsCentralContext && _currentCenter.CenterId.HasValue);
        var auditCenterId = contextCenterId
            ?? (_currentCenter.IsCentralContext ? null : _currentCenter.CenterId);

        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = actorId,
            ContextScope = hasCenter && auditCenterId.HasValue
                ? CenterClaimTypes.Center
                : CenterClaimTypes.Central,
            CenterId = hasCenter ? auditCenterId : null,
            EventType = "READ",
            Result = "SUCCESS",
            EntityType = "UserEffectiveAccess",
            EntityId = targetUserId,
            ReasonText = "اطلاع إداري على الصلاحيات الفعلية للمستخدم",
            NewValues = JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["targetUserId"] = targetUserId,
                ["contextCenterId"] = contextCenterId?.ToString()
            }),
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private string RequireActor()
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId) || _currentUser.UserId == "0")
            throw new UnauthenticatedUserException();
        return _currentUser.UserId;
    }
}
