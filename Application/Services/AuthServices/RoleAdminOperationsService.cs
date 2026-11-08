using Application.Common.Localization;
using Application.Common.Permissions;
using Application.Common.Persistence;
using Application.Common.Results;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.AuthServices;

/// <summary>
/// Read model فوق AuditEvent لعرض العمليات الإدارية على دور محدد، مع إعادة استخدام عقد العرض المشترك.
/// </summary>
public sealed class RoleAdminOperationsService : IRoleAdminOperationsService
{
    private readonly IAuditLogRepository _audit;
    private readonly IRoleRepository _roles;
    private readonly IUserRepository _users;
    private readonly IPermissionGate _permissionGate;

    public RoleAdminOperationsService(
        IAuditLogRepository audit,
        IRoleRepository roles,
        IUserRepository users,
        IPermissionGate permissionGate)
    {
        _audit = audit;
        _roles = roles;
        _users = users;
        _permissionGate = permissionGate;
    }

    public async Task<PaginatedList<UserAdminOperationDto>> GetForRoleAsync(
        string roleId,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(SystemPermissions.AuditLog.Read);

        if (string.IsNullOrWhiteSpace(roleId))
            throw new InvalidUserDataException("معرّف الدور مطلوب.");

        var role = await _roles.GetByIdAsync(roleId.Trim(), asNoTracking: true, cancellationToken)
            ?? throw new RoleNotFoundException("الدور غير موجود.");

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var filtered = QueryForTargetRole(_audit.Query(), role.Id);
        var total = await filtered.CountAsync(cancellationToken);
        var raw = await filtered
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.EventId)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // EventId فريد يمنع تكرار نفس السجل إن وُجد أكثر من مسار قراءة.
        var mapped = new List<UserAdminOperationDto>();
        var seen = new HashSet<long>();
        foreach (var ev in raw)
        {
            if (!seen.Add(ev.EventId))
                continue;

            var (title, detail) = RoleAdminOperationTitles.TryMap(ev);
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

        await FillActorNamesAsync(mapped, cancellationToken);
        return new PaginatedList<UserAdminOperationDto>(mapped, total, pageNumber, pageSize);
    }

    /// <summary>
    /// أحداث الدور المستهدف: كيان Role مباشرة، أو RolePermission/RoleFieldPermission عبر RoleId في القيم.
    /// </summary>
    public static IQueryable<AuditEvent> QueryForTargetRole(IQueryable<AuditEvent> query, string roleId)
    {
        var needle = "\"RoleId\":\"" + roleId + "\"";
        // jsonb لا يقبل عامل LIKE مباشرة؛ AuditJsonbText.Contains يُترجم إلى (jsonb)::text LIKE.
        return query.Where(e =>
            (e.EntityType == nameof(Role) && e.EntityId == roleId)
            || (e.EntityType == nameof(RolePermission) && (
                AuditJsonbText.Contains(e.NewValues, needle)
                || AuditJsonbText.Contains(e.OldValues, needle)))
            || (e.EntityType == nameof(RoleFieldPermission) && (
                AuditJsonbText.Contains(e.NewValues, needle)
                || AuditJsonbText.Contains(e.OldValues, needle))));
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
