using Application.Common.Results;
using Application.Common.Utilities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.ModelsDto.AuditLogDtos;
using MapsterMapper;
using Domain.Entities.Audit;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly IAuditLogRepository _auditLogs;
        private readonly IUserRepository _users;
        private readonly ICurrentUserService _currentUser;
        private readonly IMapper _mapper;

        public AuditLogService(
            IAuditLogRepository auditLogs,
            IUserRepository users,
            IMapper mapper,
            ICurrentUserService currentUser)
        {
            _auditLogs = auditLogs;
            _users = users;
            _mapper = mapper;
            _currentUser = currentUser;
        }

        #region استرجاع سجل حسب المعرف (يستخدم AuditLogDetailsDto)
        public async Task<AuditLogDetailsDto> GetByIdAsync(long id)
        {
            var scoped = _auditLogs.Query()
                         .Where(a => a.EventId == id);

            var entity = await scoped.FirstOrDefaultAsync()
                ?? throw new ReportNotFoundException("سجل التدقيق غير موجود.");

            var dto = _mapper.Map<AuditLogDetailsDto>(entity);
            await FillNamesAsync(new[] { dto });
            return dto;
        }

        #endregion

        #region استرجاع جميع السجلات مع دعم pagination (يستخدم AuditLogListDto)
        public async Task<PaginatedList<AuditLogListDto>> GetAllAsync(int pageNumber, int pageSize)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var scoped = _auditLogs.Query()
                         .OrderByDescending(a => a.OccurredAt);

            var total = await scoped.CountAsync();

            var items = await scoped
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var mapped = _mapper.Map<List<AuditLogListDto>>(items);
            await FillNamesAsync(mapped);
            return new PaginatedList<AuditLogListDto>(mapped, total, pageNumber, pageSize);
        }

        #endregion

        #region    استرجاع حميع السجلات مع فلترة و pagination
        public async Task<PaginatedList<AuditLogListDto>> GetAllFilteredAsync(int pageNumber, int pageSize, AuditLogFilter auditLogFilter)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            auditLogFilter ??= new AuditLogFilter();
            var scoped = _auditLogs.Query();

            if (!string.IsNullOrWhiteSpace(auditLogFilter.searchUserName))
                scoped = await FilterByActorNameAsync(scoped, auditLogFilter.searchUserName);

            if (!string.IsNullOrWhiteSpace(auditLogFilter.userName))
                scoped = await FilterByActorNameAsync(scoped, auditLogFilter.userName);

            if (auditLogFilter.actionType.HasValue)
            {
                var at = auditLogFilter.actionType.Value.ToString();
                scoped = scoped.Where(a => a.EventType == at);
            }

            if (auditLogFilter.tableNames != null && auditLogFilter.tableNames.Count > 0)
            {
                var set = auditLogFilter.tableNames
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim().ToLower())
                    .Distinct()
                    .ToList();

                if (set.Count > 0)
                    scoped = scoped.Where(a => set.Contains((a.EntityType ?? "").ToLower()));
            }

            ApplyDateRange(ref scoped, auditLogFilter.From, auditLogFilter.To);

            scoped = scoped.OrderByDescending(a => a.OccurredAt);

            var total = await scoped.CountAsync();

            var list = await scoped
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var mapped = _mapper.Map<List<AuditLogListDto>>(list);
            await FillNamesAsync(mapped);

            return new PaginatedList<AuditLogListDto>(mapped, total, pageNumber, pageSize);
        }

        private async Task<IQueryable<AuditEvent>> FilterByActorNameAsync(IQueryable<AuditEvent> query, string raw)
        {
            var term = SqlLikePatternEscape.EscapeForSqlServerLike(raw.Trim().ToLower());
            var ids = await _users.QueryUsers()
                .Where(u => (u.FullName ?? "").ToLower().Contains(term) || (u.UserName ?? "").ToLower().Contains(term))
                .Select(u => u.Id)
                .ToListAsync();
            return query.Where(a => ids.Contains(a.ActorUserId!) || (a.ActorUserId ?? "").ToLower().Contains(term));
        }

        private async Task FillNamesAsync(IEnumerable<AuditLogListDto> items)
        {
            var list = items.ToList();
            var names = await _users.GetDisplayNamesByIdsAsync(list.Select(x => x.UserId).Where(id => !string.IsNullOrWhiteSpace(id)));
            foreach (var item in list)
            {
                if (names.TryGetValue(item.UserId, out var name))
                    item.UserName = name;
            }
        }

        private static void ApplyDateRange(ref IQueryable<AuditEvent> q, DateTimeOffset? from, DateTimeOffset? to)
        {
            if (!from.HasValue && !to.HasValue) return;

            DateTimeOffset? start = null;
            DateTimeOffset? endExclusive = null;

            if (from.HasValue)
            {
                var f = from.Value;
                start = (f.TimeOfDay == TimeSpan.Zero) ? new DateTimeOffset(f.Date, f.Offset) : f;
            }

            if (to.HasValue)
            {
                var t = to.Value;
                endExclusive = (t.TimeOfDay == TimeSpan.Zero)
                    ? new DateTimeOffset(t.Date.AddDays(1), t.Offset)
                    : t;
            }

            if (start.HasValue) q = q.Where(a => a.OccurredAt >= start.Value);
            if (endExclusive.HasValue) q = q.Where(a => a.OccurredAt < endExclusive.Value);
        }

        #endregion

        #region   جلب ملخص إحصائي للسجلات
        public async Task<AuditLogSummaryDto> GetSummaryAsync()
        {
            var scoped = _auditLogs.Query();

            var grouped = await scoped
                .GroupBy(a => a.EventType)
                .Select(g => new { ActionType = g.Key, Count = g.LongCount() })
                .ToListAsync();

            long added = grouped.FirstOrDefault(x => x.ActionType == "Added")?.Count ?? 0;
            long modified = grouped.FirstOrDefault(x => x.ActionType == "Modified")?.Count ?? 0;
            long deleted = grouped.FirstOrDefault(x => x.ActionType == "Deleted")?.Count ?? 0;
            long total = added + modified + deleted;

            return new AuditLogSummaryDto
            {
                Total = total,
                Added = added,
                Modified = modified,
                Deleted = deleted
            };
        }

        public Task<List<string>> GetEntityTypeNamesAsync()
            => _auditLogs.GetEntityTypeNamesAsync();

        #endregion
}
}
