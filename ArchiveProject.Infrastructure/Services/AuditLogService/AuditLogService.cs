using ArchiveProject.Application.Common.Interfaces.AuditLogInterfaces;
using ArchiveProject.Application.Common.Utilities;
using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.ModelsDto.AuditLogDtos;
using ArchiveProject.Domain.Entities;
using ArchiveProject.Infrastructure.Data;
using AutoMapper;
using Microsoft.EntityFrameworkCore;

namespace FinanceProject.Infrastructure.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly MainDbContext _context;
        private readonly IMapper _mapper;
        private readonly ICurrentUserService _currentUser;
        public AuditLogService(MainDbContext context, IMapper mapper,ICurrentUserService currentUser)
        {
            _context = context;
            _mapper = mapper;
            _currentUser = currentUser;
        }

        #region استرجاع سجل حسب المعرف (يستخدم AuditLogDetailsDto)
        public async Task<Result<AuditLogDetailsDto>> GetByIdAsync(long id)
        {
            var (isSuper, isMgr, userId) = await GetActorScopeAsync();

            var scoped = ApplyScope(_context.AuditLogs.AsNoTracking(), isSuper, isMgr, userId)
                         .Where(a => a.LogId == id);

            var entity = await scoped.FirstOrDefaultAsync();
            if (entity == null)
                return Result<AuditLogDetailsDto>.Failure(new List<string> { "Not found" }, null);

            return Result<AuditLogDetailsDto>.Success(_mapper.Map<AuditLogDetailsDto>(entity));
        }

        #endregion

        #region استرجاع جميع السجلات مع دعم pagination (يستخدم AuditLogListDto)
        public async Task<Result<PaginatedList<AuditLogListDto>>> GetAllAsync(int pageNumber, int pageSize)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var (isSuper, isMgr, userId) = await GetActorScopeAsync();

            var scoped = ApplyScope(_context.AuditLogs.AsNoTracking(), isSuper, isMgr, userId)
                         .OrderByDescending(a => a.Timestamp);

            var total = await scoped.CountAsync();

            var items = await scoped
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var mapped = _mapper.Map<List<AuditLogListDto>>(items);
            return Result<PaginatedList<AuditLogListDto>>.Success(new(mapped, total, pageNumber, pageSize));
        }

        #endregion

        #region    استرجاع حميع السجلات مع فلترة و pagination
        public async Task<Result<PaginatedList<AuditLogListDto>>> GetAllFilteredAsync(int pageNumber,int pageSize,AuditLogFilter auditLogFilter)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            auditLogFilter ??= new AuditLogFilter();

            var (isSuper, isMgr, userId) = await GetActorScopeAsync();

            var scoped = ApplyScope(_context.AuditLogs.AsNoTracking(), isSuper, isMgr, userId);

            // ========= searchUserName (بحث بالاسم فقط) =========
            if (!string.IsNullOrWhiteSpace(auditLogFilter.searchUserName))
            {
                var s = SqlLikePatternEscape.EscapeForSqlServerLike(auditLogFilter.searchUserName.Trim().ToLower());
                scoped = scoped.Where(a =>
                    (a.UserName ?? "").ToLower().Contains(s));
            }

            // ========= userName (اسم أو Id) =========
            if (!string.IsNullOrWhiteSpace(auditLogFilter.userName))
            {
                var s = SqlLikePatternEscape.EscapeForSqlServerLike(auditLogFilter.userName.Trim().ToLower());
                scoped = scoped.Where(a =>
                    (a.UserName ?? "").ToLower().Contains(s) ||
                    (a.UserId ?? "").ToLower().Contains(s));
            }

            // ========= actionType =========
            if (auditLogFilter.actionType.HasValue)
            {
                var at = auditLogFilter.actionType.Value.ToString(); // Added/Modified/Deleted...
                scoped = scoped.Where(a => a.ActionType == at);
            }

            // ========= tableNames =========
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

            // ========= Date Range: From/To (محلياً) =========
            ApplyDateRange(ref scoped, auditLogFilter.From, auditLogFilter.To);

            scoped = scoped.OrderByDescending(a => a.Timestamp);

            var total = await scoped.CountAsync();

            var list = await scoped
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var mapped = _mapper.Map<List<AuditLogListDto>>(list);

            return Result<PaginatedList<AuditLogListDto>>.Success(
                new PaginatedList<AuditLogListDto>(mapped, total, pageNumber, pageSize));
        }


        // ===== Helper: Date Range (local) =====
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
        public async Task<Result<AuditLogSummaryDto>> GetSummaryAsync()
        {
            var (isSuper, isMgr, userId) = await GetActorScopeAsync();

            var scoped = ApplyScope(_context.AuditLogs.AsNoTracking(), isSuper, isMgr, userId);

            var grouped = await scoped
                .GroupBy(a => a.ActionType)
                .Select(g => new { ActionType = g.Key, Count = g.LongCount() })
                .ToListAsync();

            long added = grouped.FirstOrDefault(x => x.ActionType == "Added")?.Count ?? 0;
            long modified = grouped.FirstOrDefault(x => x.ActionType == "Modified")?.Count ?? 0;
            long deleted = grouped.FirstOrDefault(x => x.ActionType == "Deleted")?.Count ?? 0;
            long total = added + modified + deleted;

            return Result<AuditLogSummaryDto>.Success(new AuditLogSummaryDto
            {
                Total = total,
                Added = added,
                Modified = modified,
                Deleted = deleted
            });
        }

        #endregion




        /// <summary>
        /// يطبّق نطاق الوصول على استعلام السجلات.
        /// - SuperAdmin و BranchManager: بلا فلترة
        /// - غير ذلك: سجلات المستخدم الحالي فقط
        /// </summary>
        private async Task<(bool IsSuperAdmin, bool IsBranchManager, string UserId)> GetActorScopeAsync()
        {
            var userId = _currentUser.UserId ?? string.Empty;

            // أدوار الممثل (NormalizedName)
            var roles = await _context.UserRoleAssociations
                .Where(ura => ura.UserId == userId)
                .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                .ToListAsync();

            bool isSuperAdmin = roles.Any(n => n == "SUPERADMIN");
            bool isBranchManager = roles.Any(n => n == "BRANCHMANAGER");

            return (isSuperAdmin, isBranchManager, userId);
        }

        private IQueryable<AuditLog> ApplyScope(IQueryable<AuditLog> baseQuery, bool isSuperAdmin, bool isBranchManager, string userId)
        {
            if (isSuperAdmin || isBranchManager) return baseQuery;
            return baseQuery.Where(a => a.UserId == userId);
        }
    }
}

