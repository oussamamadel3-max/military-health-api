using Application.Common.Localization;
using Application.Common.Permissions;
using Application.Common.Results;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.AuthServices;

/// <summary>
/// Read model فوق AuditEvent لعرض العمليات الإدارية على حساب مستخدم محدد.
/// </summary>
public sealed class UserAdminOperationsService : IUserAdminOperationsService
{
    private readonly IAuditLogRepository _audit;
    private readonly IUserRepository _users;
    private readonly IAccessContextRepository _access;
    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;

    public UserAdminOperationsService(
        IAuditLogRepository audit,
        IUserRepository users,
        IAccessContextRepository access,
        IPermissionGate permissionGate,
        ICurrentUserService currentUser)
    {
        _audit = audit;
        _users = users;
        _access = access;
        _permissionGate = permissionGate;
        _currentUser = currentUser;
    }

    public async Task<PaginatedList<UserAdminOperationDto>> GetForUserAsync(
        string userId,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(SystemPermissions.AuditLog.Read);

        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidUserDataException("معرّف المستخدم مطلوب.");

        var exists = await _users.FindByIdAsync(userId);
        if (exists is null)
            throw new UserNotFoundException(userId);

        var actorId = _currentUser.UserId ?? string.Empty;
        await UserManagementScopeGuard.EnsureCanManageTargetAsync(
            _access, _users, actorId, userId, cancellationToken);

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        // نافذة محدودة ثم ترجمة؛ بعض أحداث Modified تُستبعد (مثل MustChangePassword وحده).
        var fetchSize = Math.Min(500, Math.Max(pageNumber * pageSize * 4, 80));
        var raw = await QueryForTargetUser(_audit.Query(), userId)
            .OrderByDescending(e => e.OccurredAt)
            .Take(fetchSize)
            .ToListAsync(cancellationToken);

        var mapped = new List<UserAdminOperationDto>();
        foreach (var ev in raw)
        {
            var (title, detail) = UserAdminOperationTitles.TryMap(ev);
            if (string.IsNullOrWhiteSpace(title))
                continue;

            mapped.Add(new UserAdminOperationDto
            {
                EventId = ev.EventId,
                EventType = ev.EventType,
                Title = title,
                Detail = detail,
                ActorUserId = ev.ActorUserId,
                OccurredAt = ev.OccurredAt,
                Result = ev.Result
            });
        }

        var page = mapped
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        await FillActorNamesAsync(page, cancellationToken);
        return new PaginatedList<UserAdminOperationDto>(page, mapped.Count, pageNumber, pageSize);
    }

    /// <summary>
    /// أحداث مرتبطة بالمستخدم المستهدف عبر TargetUserId أو توافق تراثي (EntityId / JSON UserId).
    /// </summary>
    public static IQueryable<AuditEvent> QueryForTargetUser(IQueryable<AuditEvent> query, string userId)
    {
        var related = new[]
        {
            nameof(User),
            nameof(UserRoleAssociation),
            nameof(UserPermission),
            nameof(UserFieldPermission),
            nameof(UserCenter),
            nameof(UserClinicAssignment)
        };

        var explicitTypes = new[]
        {
            UserAccountAdminEventTypes.PasswordReset,
            UserAccountAdminEventTypes.SessionsInvalidated,
            UserAccountAdminEventTypes.AccountLocked,
            UserAccountAdminEventTypes.AccountUnlocked
        };

        var needle = "\"UserId\":\"" + userId + "\"";
        var centerPrefix = userId + ":";

        return query.Where(e =>
            e.TargetUserId == userId
            || (explicitTypes.Contains(e.EventType) && e.EntityId == userId)
            || (e.EntityType == nameof(User) && e.EntityId == userId)
            || (related.Contains(e.EntityType!) && (
                (e.NewValues != null && e.NewValues.Contains(needle))
                || (e.OldValues != null && e.OldValues.Contains(needle))
                || (e.EntityType == nameof(UserCenter) && e.EntityId != null && e.EntityId.StartsWith(centerPrefix)))));
    }

    private async Task FillActorNamesAsync(List<UserAdminOperationDto> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(i => i.ActorUserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct()!;
        var names = await _users.GetDisplayNamesByIdsAsync(ids!, cancellationToken);
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.ActorUserId) &&
                names.TryGetValue(item.ActorUserId, out var name))
            {
                item.ActorDisplayName = name;
            }
            else if (string.IsNullOrWhiteSpace(item.ActorUserId))
            {
                item.ActorDisplayName = "النظام";
            }
        }
    }
}
