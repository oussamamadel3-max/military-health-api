using Application.Common.Utilities;
using Application.Interfaces.Repositories;
using Application.ModelsDto.AuditLogDtos;
using Domain.Entities.Audit;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// تطبيق فلاتر سجل التدقيق المشتركة (FR-077): مستخدم، عملية، مركز، فترة، نوع المورد.
/// </summary>
public static class AuditEventQuery
{
    public static async Task<IQueryable<AuditEvent>> ApplyAsync(
        IQueryable<AuditEvent> query,
        AuditLogFilter filter,
        IUserRepository users,
        CancellationToken cancellationToken = default)
    {
        filter ??= new AuditLogFilter();

        if (!string.IsNullOrWhiteSpace(filter.actorUserId))
        {
            var actorId = filter.actorUserId.Trim();
            query = query.Where(a => a.ActorUserId == actorId);
        }

        if (!string.IsNullOrWhiteSpace(filter.searchUserName))
            query = await FilterByActorNameAsync(query, filter.searchUserName, users, cancellationToken);

        if (!string.IsNullOrWhiteSpace(filter.userName))
            query = await FilterByActorNameAsync(query, filter.userName, users, cancellationToken);

        if (!string.IsNullOrWhiteSpace(filter.eventType))
        {
            var eventType = filter.eventType.Trim();
            query = query.Where(a => a.EventType == eventType);
        }
        else if (filter.actionType.HasValue)
        {
            var at = filter.actionType.Value.ToString();
            query = query.Where(a => a.EventType == at);
        }

        if (filter.centerId.HasValue)
            query = query.Where(a => a.CenterId == filter.centerId.Value);

        if (filter.tableNames is { Count: > 0 })
        {
            var set = filter.tableNames
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLower())
                .Distinct()
                .ToList();

            if (set.Count > 0)
                query = query.Where(a => set.Contains((a.EntityType ?? "").ToLower()));
        }
        else if (!string.IsNullOrWhiteSpace(filter.entityType))
        {
            var entity = filter.entityType.Trim().ToLower();
            query = query.Where(a => (a.EntityType ?? "").ToLower() == entity);
        }

        ApplyDateRange(ref query, filter.From, filter.To);
        return query;
    }

    private static async Task<IQueryable<AuditEvent>> FilterByActorNameAsync(
        IQueryable<AuditEvent> query,
        string raw,
        IUserRepository users,
        CancellationToken cancellationToken)
    {
        var term = SqlLikePatternEscape.EscapeForSqlServerLike(raw.Trim().ToLower());
        var ids = await users.QueryUsers()
            .Where(u => (u.FullName ?? "").ToLower().Contains(term) || (u.UserName ?? "").ToLower().Contains(term))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        return query.Where(a => ids.Contains(a.ActorUserId!) || (a.ActorUserId ?? "").ToLower().Contains(term));
    }

    private static void ApplyDateRange(ref IQueryable<AuditEvent> q, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (!from.HasValue && !to.HasValue) return;

        DateTimeOffset? start = null;
        DateTimeOffset? endExclusive = null;

        if (from.HasValue)
        {
            var f = from.Value;
            start = f.TimeOfDay == TimeSpan.Zero ? new DateTimeOffset(f.Date, f.Offset) : f;
        }

        if (to.HasValue)
        {
            var t = to.Value;
            endExclusive = t.TimeOfDay == TimeSpan.Zero
                ? new DateTimeOffset(t.Date.AddDays(1), t.Offset)
                : t;
        }

        if (start.HasValue) q = q.Where(a => a.OccurredAt >= start.Value);
        if (endExclusive.HasValue) q = q.Where(a => a.OccurredAt < endExclusive.Value);
    }
}
