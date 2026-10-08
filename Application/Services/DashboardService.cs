using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Dtos.DashboardDtos;
using Domain.Entities;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Dashboard
{
    public class DashboardService : IDashboardService
    {
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IUserContextService _userCtx;
        private readonly IMapper _mapper;

        public DashboardService(
            IAuditLogRepository auditLogRepository,
            IUserContextService userCtx,
            IMapper mapper)
        {
            _auditLogRepository = auditLogRepository;
            _userCtx = userCtx;
            _mapper = mapper;
        }

        // ======== Date Range Helper (Europe/Istanbul -> UTC DateTimeOffset) ========
        private static (DateTimeOffset? Start, DateTimeOffset? EndExclusive, string? PeriodText)
            NormalizeRange(DateTime? from, DateTime? to)
        {
            if (!from.HasValue && !to.HasValue)
                return (null, null, null);

            var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

            DateTimeOffset? start = null;
            DateTimeOffset? endEx = null;

            if (from.HasValue)
            {
                var f = DateTime.SpecifyKind(from.Value, DateTimeKind.Unspecified);
                start = new DateTimeOffset(f, tz.GetUtcOffset(f));
            }

            if (to.HasValue)
            {
                var t = DateTime.SpecifyKind(to.Value, DateTimeKind.Unspecified);
                endEx = new DateTimeOffset(t, tz.GetUtcOffset(t));

                if (to.Value.TimeOfDay == TimeSpan.Zero)
                    endEx = endEx.Value.AddDays(1);
            }

            if (start.HasValue && !endEx.HasValue)
                endEx = DateTimeOffset.UtcNow;

            if (!start.HasValue && endEx.HasValue)
                start = DateTimeOffset.MinValue;

            string? text = null;
            if (from.HasValue && to.HasValue)
                text = $"{from:yyyy/MM/dd} → {to:yyyy/MM/dd}";
            else if (from.HasValue)
                text = $"من {from:yyyy/MM/dd}";
            else if (to.HasValue)
                text = $"حتى {to:yyyy/MM/dd}";

            return (start, endEx, text);
        }

        private static IQueryable<T> ApplyRange<T>(
            IQueryable<T> q,
            DateTimeOffset? start,
            DateTimeOffset? endEx,
            System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> selector)
        {
            if (start.HasValue)
                q = q.Where(BuildGreaterOrEqual(selector, start.Value));
            if (endEx.HasValue)
                q = q.Where(BuildLessThan(selector, endEx.Value));
            return q;
        }

        private static System.Linq.Expressions.Expression<Func<T, bool>> BuildGreaterOrEqual<T>(
            System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> selector,
            DateTimeOffset value)
        {
            var p = selector.Parameters[0];
            var body = System.Linq.Expressions.Expression.GreaterThanOrEqual(
                selector.Body,
                System.Linq.Expressions.Expression.Constant(value));
            return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, p);
        }

        private static System.Linq.Expressions.Expression<Func<T, bool>> BuildLessThan<T>(
            System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> selector,
            DateTimeOffset value)
        {
            var p = selector.Parameters[0];
            var body = System.Linq.Expressions.Expression.LessThan(
                selector.Body,
                System.Linq.Expressions.Expression.Constant(value));
            return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, p);
        }
public async Task<DashboardStatsDto> GetAsync(DashboardFilter f)
        {
            var (start, endEx, _) = NormalizeRange(f.From, f.To);

            var auditQ = _auditLogRepository.Query();

            if (start.HasValue || endEx.HasValue)
                auditQ = ApplyRange(auditQ, start, endEx, a => a.Timestamp);

            var recentLogs = await auditQ
                .OrderByDescending(a => a.Timestamp)
                .Take(f.RecentCount <= 0 ? 10 : f.RecentCount)
                .ToListAsync();

            var recent = _mapper.Map<List<RecentActivityDto>>(recentLogs);

            var topUsersRaw = await (from a in auditQ
                                     group a by new { a.UserId, a.UserName } into g
                                     select new
                                     {
                                         UserId = g.Key.UserId ?? "",
                                         UserName = g.Key.UserName ?? "",
                                         ActionsCount = g.LongCount(),
                                         LastActivity = g.Max(x => x.Timestamp)
                                     })
                .OrderByDescending(x => x.ActionsCount)
                .ThenByDescending(x => x.LastActivity)
                .Take(f.TopUsersCount <= 0 ? 5 : f.TopUsersCount)
                .ToListAsync();

            var topUsers = topUsersRaw.Select(x => new ActiveUserStatsDto
            {
                UserId = x.UserId,
                UserName = x.UserName,
                ActionsCount = x.ActionsCount,
                LastActivityAt = x.LastActivity.UtcDateTime.ToString("o")
            }).ToList();

            return new DashboardStatsDto
            {
                RecentActivities = recent,
                MostActiveUsers = topUsers,
            };
        }
    }
}
