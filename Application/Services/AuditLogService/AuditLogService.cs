using Application.Common.Results;
using Application.Common.Utilities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.ModelsDto.AuditLogDtos;
using MapsterMapper;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly IAuditLogRepository _auditLogs;
        private readonly IActorScopeProvider _actorScope;
        private readonly ICurrentUserService _currentUser;
        private readonly IMapper _mapper;

        public AuditLogService(
            IAuditLogRepository auditLogs,
            IActorScopeProvider actorScope,
            IMapper mapper,
            ICurrentUserService currentUser)
        {
            _auditLogs = auditLogs;
            _actorScope = actorScope;
            _mapper = mapper;
            _currentUser = currentUser;
        }

        #region استرجاع سجل حسب المعرف (يستخدم AuditLogDetailsDto)
        public async Task<AuditLogDetailsDto> GetByIdAsync(long id)
        {
            var scope = await GetActorScopeAsync();

            var scoped = ApplyScope(_auditLogs.Query(), scope)
                         .Where(a => a.LogId == id);

            var entity = await scoped.FirstOrDefaultAsync()
                ?? throw new ReportNotFoundException("سجل التدقيق غير موجود.");

            return _mapper.Map<AuditLogDetailsDto>(entity);
        }

        #endregion

        #region استرجاع جميع السجلات مع دعم pagination (يستخدم AuditLogListDto)
        public async Task<PaginatedList<AuditLogListDto>> GetAllAsync(int pageNumber, int pageSize)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var scope = await GetActorScopeAsync();

            var scoped = ApplyScope(_auditLogs.Query(), scope)
                         .OrderByDescending(a => a.Timestamp);

            var total = await scoped.CountAsync();

            var items = await scoped
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var mapped = _mapper.Map<List<AuditLogListDto>>(items);
            return new PaginatedList<AuditLogListDto>(mapped, total, pageNumber, pageSize);
        }

        #endregion

        #region    استرجاع حميع السجلات مع فلترة و pagination
        public async Task<PaginatedList<AuditLogListDto>> GetAllFilteredAsync(int pageNumber, int pageSize, AuditLogFilter auditLogFilter)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            auditLogFilter ??= new AuditLogFilter();

            var scope = await GetActorScopeAsync();

            var scoped = ApplyScope(_auditLogs.Query(), scope);

            if (!string.IsNullOrWhiteSpace(auditLogFilter.searchUserName))
            {
                var s = SqlLikePatternEscape.EscapeForSqlServerLike(auditLogFilter.searchUserName.Trim().ToLower());
                scoped = scoped.Where(a =>
                    (a.UserName ?? "").ToLower().Contains(s));
            }

            if (!string.IsNullOrWhiteSpace(auditLogFilter.userName))
            {
                var s = SqlLikePatternEscape.EscapeForSqlServerLike(auditLogFilter.userName.Trim().ToLower());
                scoped = scoped.Where(a =>
                    (a.UserName ?? "").ToLower().Contains(s) ||
                    (a.UserId ?? "").ToLower().Contains(s));
            }

            if (auditLogFilter.actionType.HasValue)
            {
                var at = auditLogFilter.actionType.Value.ToString();
                scoped = scoped.Where(a => a.ActionType == at);
            }

            if (auditLogFilter.tableNames != null && auditLogFilter.tableNames.Count > 0)
            {
                var set = auditLogFilter.tableNames
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim().ToLower())
                    .Distinct()
                    .ToList();

                if (set.Count > 0)
                    scoped = scoped.Where(a => set.Contains((a.TableName ?? "").ToLower()));
            }

            ApplyDateRange(ref scoped, auditLogFilter.From, auditLogFilter.To);

            scoped = scoped.OrderByDescending(a => a.Timestamp);

            var total = await scoped.CountAsync();

            var list = await scoped
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var mapped = _mapper.Map<List<AuditLogListDto>>(list);

            return new PaginatedList<AuditLogListDto>(mapped, total, pageNumber, pageSize);
        }

        private static void ApplyDateRange(ref IQueryable<AuditLog> q, DateTimeOffset? from, DateTimeOffset? to)
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

            if (start.HasValue) q = q.Where(a => a.Timestamp >= start.Value);
            if (endExclusive.HasValue) q = q.Where(a => a.Timestamp < endExclusive.Value);
        }

        #endregion

        #region   جلب ملخص إحصائي للسجلات
        public async Task<AuditLogSummaryDto> GetSummaryAsync()
        {
            var scope = await GetActorScopeAsync();

            var scoped = ApplyScope(_auditLogs.Query(), scope);

            var grouped = await scoped
                .GroupBy(a => a.ActionType)
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

        #endregion

        private async Task<ActorScope> GetActorScopeAsync()
        {
            var userId = _currentUser.UserId ?? string.Empty;
            return await _actorScope.GetScopeAsync(userId);
        }

        private static IQueryable<AuditLog> ApplyScope(IQueryable<AuditLog> baseQuery, ActorScope scope)
        {
            if (scope.IsSuperAdmin) return baseQuery;
            return baseQuery.Where(a => a.UserId == scope.UserId);
        }
    }
}
