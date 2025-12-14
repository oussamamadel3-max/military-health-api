using ArchiveProject.Application.Common.Interfaces.AuditLogInterfaces;
using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.ModelsDto.AuditLogDtos;
using ArchiveProject.Domain.Entities;
using ArchiveProject.Infrastructure.Data;
using AutoMapper;
using AutoMapper.QueryableExtensions;
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
        //public async Task<Result<AuditLogDetailsDto>> GetByIdAsync(long id)
        //{
        //    // هنا يجب التأكد من جلب الكيان كاملاً لعملية الـ Mapping
        //    var entity = await _context.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.LogId == id);

        //    if (entity == null)
        //        return Result<AuditLogDetailsDto>.Failure(new List<string> { "Not found" }, null);

        //    // Mapping إلى DTO التفاصيل الذي يحتوي على OldValue/NewValue
        //    return Result<AuditLogDetailsDto>.Success(_mapper.Map<AuditLogDetailsDto>(entity));
        //}
        public async Task<Result<AuditLogDetailsDto>> GetByIdAsync(long id)
        {
            var (isSuper, isMgr, brId, depId, userId) = await GetActorScopeAsync();

            var scoped = ApplyScope(_context.AuditLogs.AsNoTracking(), isSuper, isMgr, brId, depId, userId)
                         .Where(a => a.LogId == id);

            var entity = await scoped.FirstOrDefaultAsync();
            if (entity == null)
                return Result<AuditLogDetailsDto>.Failure(new List<string> { "Not found" }, null);

            return Result<AuditLogDetailsDto>.Success(_mapper.Map<AuditLogDetailsDto>(entity));
        }

        #endregion

        #region استرجاع جميع السجلات مع دعم pagination (يستخدم AuditLogListDto)
        //public async Task<Result<PaginatedList<AuditLogListDto>>> GetAllAsync(int pageNumber, int pageSize)
        //{
        //    // الترتيب حسب الوقت (الأحدث أولاً)
        //    var query = _context.AuditLogs.AsNoTracking().OrderByDescending(a => a.Timestamp);
        //    var total = await query.CountAsync();

        //    var list = await query
        //        .Skip((pageNumber - 1) * pageSize)
        //        .Take(pageSize)
        //        .ToListAsync();

        //    // Mapping إلى DTO القائمة (لا يتم تضمين OldValue/NewValue)
        //    var mapped = _mapper.Map<List<AuditLogListDto>>(list);
        //    var paginated = new PaginatedList<AuditLogListDto>(mapped, total, pageNumber, pageSize);

        //    return Result<PaginatedList<AuditLogListDto>>.Success(paginated);
        //}
        public async Task<Result<PaginatedList<AuditLogListDto>>> GetAllAsync(int pageNumber, int pageSize)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var (isSuper, isMgr, brId, depId, userId) = await GetActorScopeAsync();

            var scoped = ApplyScope(_context.AuditLogs.AsNoTracking(), isSuper, isMgr, brId, depId, userId)
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
        //public async Task<Result<PaginatedList<AuditLogListDto>>> GetAllFilteredAsync(
        //    int pageNumber, int pageSize,
        //    string? userName,
        //    AuditActionType? actionType,
        //    List<string>? tableNames,
        //    DateTime? date)
        //{
        //    if (pageNumber < 1) pageNumber = 1;
        //    if (pageSize < 1) pageSize = 10;

        //    var q = _context.AuditLogs.AsNoTracking();

        //    // 1) بحث المستخدم
        //    if (!string.IsNullOrWhiteSpace(userName))
        //    {
        //        var s = userName.Trim().ToLower();
        //        q = q.Where(a =>
        //            (a.UserName ?? "").ToLower().Contains(s) ||
        //            (a.UserId ?? "").ToLower().Contains(s));
        //    }

        //    // 2) نوع العملية
        //    if (actionType.HasValue)
        //    {
        //        var actionText = actionType.Value.ToString(); // Added/Modified/Deleted
        //        q = q.Where(a => a.ActionType == actionText);
        //    }

        //    // 3) أسماء الجداول (اختيار متعدد)
        //    if (tableNames != null && tableNames.Count > 0)
        //    {
        //        var set = tableNames
        //            .Where(t => !string.IsNullOrWhiteSpace(t))
        //            .Select(t => t.Trim().ToLower())
        //            .ToList();

        //        if (set.Count > 0)
        //            q = q.Where(a => set.Contains(a.TableName.ToLower()));
        //    }

        //    // 4) التاريخ (احترام التوقيت المحلي -> Europe/Istanbul)
        //    if (date.HasValue)
        //    {
        //        // حدّد المنطقة الزمنية (يفضّل تقرأها من الإعدادات)
        //        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

        //        // اليوم المطلوب بدون نوع (Unspecified) حتى ما يعتبره .NET UTC أو Local
        //        var dayLocal = DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Unspecified);

        //        // بداية اليوم المحلي كـ DateTimeOffset حسب إزاحة المنطقة
        //        var startLocal = new DateTimeOffset(dayLocal, tz.GetUtcOffset(dayLocal));
        //        var endLocal = startLocal.AddDays(1);

        //        // قارن مباشرة بـ DateTimeOffset (EF/Npgsql يحوّلها صح)
        //        q = q.Where(a => a.Timestamp >= startLocal && a.Timestamp < endLocal);
        //    }

        //    q = q.OrderByDescending(a => a.Timestamp);

        //    var total = await q.CountAsync();

        //    var items = await q
        //        .Skip((pageNumber - 1) * pageSize)
        //        .Take(pageSize)
        //        .ProjectTo<AuditLogListDto>(_mapper.ConfigurationProvider)
        //        .ToListAsync();

        //    var page = new PaginatedList<AuditLogListDto>(items, total, pageNumber, pageSize);
        //    return Result<PaginatedList<AuditLogListDto>>.Success(page);
        //}

        public async Task<Result<PaginatedList<AuditLogListDto>>> GetAllFilteredAsync(int pageNumber, int pageSize, string? userName, AuditActionType? actionType,List<string>? tableNames, DateTime? date)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var (isSuper, isMgr, brId, depId, userId) = await GetActorScopeAsync();

            var scoped = ApplyScope(_context.AuditLogs.AsNoTracking(), isSuper, isMgr, brId, depId, userId);

            // فلتر المستخدم (اسم أو Id)
            if (!string.IsNullOrWhiteSpace(userName))
            {
                var s = userName.Trim().ToLower();
                scoped = scoped.Where(a => (a.UserName ?? "").ToLower().Contains(s) || (a.UserId ?? "").ToLower().Contains(s));
            }

            // نوع العملية
            if (actionType.HasValue)
            {
                var at = actionType.Value.ToString(); // Added/Modified/Deleted
                scoped = scoped.Where(a => a.ActionType == at);
            }

            // أسماء الجداول
            if (tableNames != null && tableNames.Count > 0)
            {
                var set = tableNames.Where(t => !string.IsNullOrWhiteSpace(t))
                                    .Select(t => t.Trim().ToLower())
                                    .ToList();
                if (set.Count > 0)
                    scoped = scoped.Where(a => set.Contains(a.TableName.ToLower()));
            }

            // التاريخ (يوم واحد محلياً)
            if (date.HasValue)
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
                var dayLocal = DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Unspecified);
                var start = new DateTimeOffset(dayLocal, tz.GetUtcOffset(dayLocal));
                var end = start.AddDays(1);
                scoped = scoped.Where(a => a.Timestamp >= start && a.Timestamp < end);
            }

            scoped = scoped.OrderByDescending(a => a.Timestamp);

            var total = await scoped.CountAsync();

            var list = await scoped
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var mapped = _mapper.Map<List<AuditLogListDto>>(list);
            return Result<PaginatedList<AuditLogListDto>>.Success(new(mapped, total, pageNumber, pageSize));
        }

        #endregion

        #region   جلب ملخص إحصائي للسجلات
        //public async Task<Result<AuditLogSummaryDto>> GetSummaryAsync()
        //{
        //    var q = _context.AuditLogs.AsNoTracking();

        //    var grouped = await q
        //        .GroupBy(a => a.ActionType)
        //        .Select(g => new { ActionType = g.Key, Count = g.LongCount() })
        //        .ToListAsync();

        //    long added = grouped.FirstOrDefault(x => x.ActionType == "Added")?.Count ?? 0;
        //    long modified = grouped.FirstOrDefault(x => x.ActionType == "Modified")?.Count ?? 0;
        //    long deleted = grouped.FirstOrDefault(x => x.ActionType == "Deleted")?.Count ?? 0;

        //    long total = added + modified + deleted;

        //    var dto = new AuditLogSummaryDto
        //    {
        //        Total = total,
        //        Added = added,
        //        Modified = modified,
        //        Deleted = deleted
        //    };

        //    return Result<AuditLogSummaryDto>.Success(dto);
        //}
        public async Task<Result<AuditLogSummaryDto>> GetSummaryAsync()
        {
            var (isSuper, isMgr, brId, depId, userId) = await GetActorScopeAsync();

            var scoped = ApplyScope(_context.AuditLogs.AsNoTracking(), isSuper, isMgr, brId, depId, userId);

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
        /// يطبّق نطاق الوصول على استعلام السجلات بالربط مع المستخدم صاحب السجل.
        /// - SuperAdmin: بلا فلترة
        /// - BranchManager: نفس الفرع
        /// - موظف قسم: نفس القسم OR سجلاته الشخصية
        /// </summary>
        private async Task<(bool IsSuperAdmin, bool IsBranchManager, long? ActorBranchId, long? ActorDepartmentId, string UserId)>GetActorScopeAsync()
        {
            var userId = _currentUser.UserId ?? string.Empty;

            // أدوار الممثل (NormalizedName)
            var roles = await _context.UserRoleAssociations
                .Where(ura => ura.UserId == userId)
                .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                .ToListAsync();

            bool isSuperAdmin = roles.Any(n => n == "SUPERADMIN");
            bool isBranchManager = roles.Any(n => n == "BRANCHMANAGER");

            var actor = await _context.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.BranchId, u.DepartmentId })
                .FirstOrDefaultAsync();

            return (isSuperAdmin, isBranchManager, actor?.BranchId, actor?.DepartmentId, userId);
        }

        private IQueryable<AuditLog> ApplyScope(IQueryable<AuditLog> baseQuery,bool isSuperAdmin, bool isBranchManager, long? branchId, long? departmentId, string userId)
        {
            if (isSuperAdmin) return baseQuery;

            var scoped = from a in baseQuery
                         join u in _context.Users.AsNoTracking() on a.UserId equals u.Id into uu
                         from u in uu.DefaultIfEmpty()
                         select new { a, u.BranchId, u.DepartmentId };

            if (isBranchManager && branchId.HasValue)
            {
                return scoped.Where(x => x.BranchId == branchId.Value).Select(x => x.a);
            }

            // موظف قسم: نفس القسم أو السجلات الشخصية
            if (departmentId.HasValue)
                return scoped.Where(x => x.DepartmentId == departmentId.Value || x.a.UserId == userId).Select(x => x.a);

            // احتياط: إن ما كان له قسم، أظهر سجلاته فقط
            return scoped.Where(x => x.a.UserId == userId).Select(x => x.a);
        }
    }
}

