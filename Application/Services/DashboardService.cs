using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Dtos.DashboardDtos;
using Application.Common.Results;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Dashboard
{
    public class DashboardService : IDashboardService
    {
        private readonly IDocumentRepository _documentRepository;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IAttachmentRepository _attachmentRepository;
        private readonly IActorScopeProvider _actorScopeProvider;
        private readonly IUserContextService _userCtx;

        public DashboardService(
            IDocumentRepository documentRepository,
            IAuditLogRepository auditLogRepository,
            IAttachmentRepository attachmentRepository,
            IActorScopeProvider actorScopeProvider,
            IUserContextService userCtx)
        {
            _documentRepository = documentRepository;
            _auditLogRepository = auditLogRepository;
            _attachmentRepository = attachmentRepository;
            _actorScopeProvider = actorScopeProvider;
            _userCtx = userCtx;
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

        private IQueryable<AuditLog> ApplyScopeAudit(IQueryable<AuditLog> q, ActorScope scope)
        {
            if (scope.IsSuperAdmin || scope.IsBranchManager) return q;
            return q.Where(a => a.UserId == scope.UserId);
        }

        public async Task<DashboardStatsDto> GetAsync(DashboardFilter f)
        {
            var userId = _userCtx.UserId ?? string.Empty;
            var actorScope = await _actorScopeProvider.GetScopeAsync(userId);

            var (start, endEx, _) = NormalizeRange(f.From, f.To);
            var startUtc = start?.UtcDateTime;
            var endUtc = endEx?.UtcDateTime;

            var documentsCount = await _documentRepository.CountScopedAsync(actorScope, startUtc, endUtc);

            var auditQ = ApplyScopeAudit(_auditLogRepository.Query(), actorScope);

            if (start.HasValue || endEx.HasValue)
                auditQ = ApplyRange(auditQ, start, endEx, a => a.Timestamp);

            var recentRaw = await auditQ
                .OrderByDescending(a => a.Timestamp)
                .Select(a => new
                {
                    a.LogId,
                    a.UserId,
                    a.UserName,
                    a.ActionType,
                    a.RecordId,
                    a.Timestamp
                })
                .Take(f.RecentCount <= 0 ? 10 : f.RecentCount)
                .ToListAsync();

            var recent = recentRaw.Select(a => new RecentActivityDto
            {
                Id = a.LogId,
                UserId = a.UserId ?? "",
                UserName = a.UserName ?? "",
                Action = a.ActionType ?? "",
                DocumentId = a.RecordId,
                DocumentTitle = null,
                Timestamp = a.Timestamp.UtcDateTime.ToString("o")
            }).ToList();

            var docIds = recent.Where(r => r.DocumentId.HasValue).Select(r => r.DocumentId!.Value).Distinct().ToList();
            if (docIds.Count > 0)
            {
                var titles = await _documentRepository.GetTitlesScopedAsync(actorScope, docIds);

                foreach (var r in recent.Where(x => x.DocumentId.HasValue))
                    if (titles.TryGetValue(r.DocumentId!.Value, out var t)) r.DocumentTitle = t;
            }

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

            DateTime growthFromUtc;
            DateTime growthToUtc;

            if (start.HasValue || endEx.HasValue)
            {
                growthFromUtc = (start ?? DateTimeOffset.UtcNow.AddMonths(-6)).UtcDateTime;
                growthToUtc = (endEx ?? DateTimeOffset.UtcNow).UtcDateTime;
            }
            else
            {
                var months = Math.Abs(f.GrowthMonths <= 0 ? 6 : f.GrowthMonths);
                growthToUtc = DateTime.UtcNow.Date.AddDays(1);
                growthFromUtc = DateTime.UtcNow.AddMonths(-months).Date;
            }

            var growthRaw = await _documentRepository.GetGrowthByMonthScopedAsync(actorScope, growthFromUtc, growthToUtc);

            var growth = growthRaw.Select(x => new DocumentGrowthPointDto
            {
                Period = $"{x.Year:D4}-{x.Month:D2}",
                Count = x.Count
            }).ToList();

            growth = FillMissingMonths(growth, growthFromUtc, growthToUtc);

            var totalBytes = await _attachmentRepository.SumFileSizeForScopedDocumentsAsync(actorScope, startUtc, endUtc);

            var storage = new StorageUsageDto
            {
                TotalBytes = totalBytes,
                TotalReadable = FormatBytes(totalBytes),
            };

            return new DashboardStatsDto
            {
                DocumentsCount = documentsCount,
                RecentActivities = recent,
                MostActiveUsers = topUsers,
                DocumentGrowth = growth,
                StorageUsage = storage,
            };
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double val = bytes;
            int i = 0;
            while (val >= 1024 && i < units.Length - 1) { val /= 1024; i++; }
            return $"{val:0.##} {units[i]}";
        }

        private static List<DocumentGrowthPointDto> FillMissingMonths(List<DocumentGrowthPointDto> data, DateTime fromUtc, DateTime toUtc)
        {
            var map = data.ToDictionary(x => x.Period, x => x.Count);
            var list = new List<DocumentGrowthPointDto>();

            var cur = new DateTime(fromUtc.Year, fromUtc.Month, 1);
            var end = new DateTime(toUtc.Year, toUtc.Month, 1);

            while (cur <= end)
            {
                var key = $"{cur.Year:D4}-{cur.Month:D2}";
                map.TryGetValue(key, out var count);
                list.Add(new DocumentGrowthPointDto { Period = key, Count = count });
                cur = cur.AddMonths(1);
            }
            return list;
        }
    }
}
