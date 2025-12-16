using ArchiveProject.Application.Common.Interfaces.DashboardInterfaces;
using ArchiveProject.Application.Common.Interfaces.UserContextInterfaces;
using ArchiveProject.Application.Dtos.DashboardDtos;
using ArchiveProject.Domain.Entities;
using ArchiveProject.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArchiveProject.Infrastructure.Services.Dashboard
{
    public class DashboardService : IDashboardService
    {
        private readonly MainDbContext _context;
        private readonly IUserContextService _userCtx;

        public DashboardService(MainDbContext context, IUserContextService userCtx)
        {
            _context = context;
            _userCtx = userCtx;
        }

        private sealed record Scope(
            bool IsSuperAdmin,
            bool IsBranchManager,
            long? BranchId,
            long? DepartmentId,
            string UserId);

        private async Task<Scope> GetScopeAsync()
        {
            var userId = _userCtx.UserId ?? string.Empty;

            var roles = await _context.UserRoleAssociations
                .Where(ura => ura.UserId == userId)
                .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                .ToListAsync();

            bool isSuper = roles.Any(r => r == "SUPERADMIN");
            bool isMgr = roles.Any(r => r == "BRANCHMANAGER");

            var actor = await _context.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.BranchId, u.DepartmentId })
                .FirstOrDefaultAsync();

            return new Scope(isSuper, isMgr, actor?.BranchId, actor?.DepartmentId, userId);
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

                // إذا المستخدم عم يرسل تاريخ فقط (00:00:00) بدنا نعتبره يوم كامل:
                if (to.Value.TimeOfDay == TimeSpan.Zero)
                    endEx = endEx.Value.AddDays(1);
            }

            // إذا from فقط
            if (start.HasValue && !endEx.HasValue)
                endEx = DateTimeOffset.UtcNow;

            // إذا to فقط
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

        // ======== Docs Scope (يدعم DepartmentId null عبر User.BranchId) ========
        private IQueryable<Document> ApplyScopeDocs(IQueryable<Document> q, Scope s)
        {
            if (s.IsSuperAdmin) return q;

            // left join departments + users to decide branch/department visibility safely
            var joined =
                from d in q
                join dep in _context.Departments.AsNoTracking()
                    on d.DepartmentId equals dep.DepartmentId into depj
                from dep in depj.DefaultIfEmpty()
                join u in _context.Users.AsNoTracking()
                    on d.UserId equals u.Id into uj
                from u in uj.DefaultIfEmpty()
                select new
                {
                    Doc = d,
                    DepId = (long?)d.DepartmentId,
                    DepBranchId = (long?)(dep == null ? null : dep.BranchId),
                    UserBranchId = (long?)(u == null ? null : u.BranchId),
                    UserId = d.UserId
                };

            if (s.IsBranchManager && s.BranchId.HasValue)
            {
                // كل وثائق الفرع: (وثائق الأقسام التي BranchId=فرعي) + (وثائق بلا قسم لكن User.BranchId=فرعي)
                var bid = s.BranchId.Value;
                return joined
                    .Where(x => (x.DepBranchId.HasValue && x.DepBranchId.Value == bid)
                             || (!x.DepBranchId.HasValue && x.UserBranchId.HasValue && x.UserBranchId.Value == bid))
                    .Select(x => x.Doc);
            }

            // Employee
            if (s.DepartmentId.HasValue)
            {
                var did = s.DepartmentId.Value;
                return joined
                    .Where(x =>
                        (x.DepId.HasValue && x.DepId.Value == did) // وثائق القسم
                        || (x.UserId == s.UserId)                  // وثائق المستخدم بأي حال
                    )
                    .Select(x => x.Doc);
            }

            return joined.Where(x => x.UserId == s.UserId).Select(x => x.Doc);
        }

        // ======== Audit Scope (مثل قبل لكن ثابت) ========
        private IQueryable<AuditLog> ApplyScopeAudit(IQueryable<AuditLog> q, Scope s)
        {
            if (s.IsSuperAdmin) return q;

            var joined = from a in q
                         join u in _context.Users.AsNoTracking() on a.UserId equals u.Id into uu
                         from u in uu.DefaultIfEmpty()
                         select new
                         {
                             Log = a,
                             BranchId = (long?)(u == null ? null : u.BranchId),
                             DepartmentId = (long?)(u == null ? null : u.DepartmentId)
                         };

            if (s.IsBranchManager && s.BranchId.HasValue)
                return joined.Where(x => x.BranchId == s.BranchId.Value).Select(x => x.Log);

            if (s.DepartmentId.HasValue)
                return joined.Where(x => x.DepartmentId == s.DepartmentId.Value || x.Log.UserId == s.UserId)
                             .Select(x => x.Log);

            return joined.Where(x => x.Log.UserId == s.UserId).Select(x => x.Log);
        }

        public async Task<Result<DashboardStatsDto>> GetAsync(DashboardFilter f)
        {
            try
            {
                var s = await GetScopeAsync();

                // ====== Date Range (optional) ======
                var (start, endEx, periodText) = NormalizeRange(f.From, f.To);

                // ====== Base Docs Query (scoped) ======
                var docsQ = ApplyScopeDocs(_context.Documents.AsNoTracking(), s);

                // filters (within scope)
                if (f.DepartmentId.HasValue)
                    docsQ = docsQ.Where(d => d.DepartmentId == f.DepartmentId.Value);

                if (f.BranchId.HasValue)
                {
                    // branch filter عبر الأقسام أو عبر User.BranchId لو الوثيقة بدون قسم
                    var bid = f.BranchId.Value;
                    docsQ =
                        from d in docsQ
                        join dep in _context.Departments.AsNoTracking()
                            on d.DepartmentId equals dep.DepartmentId into depj
                        from dep in depj.DefaultIfEmpty()
                        join u in _context.Users.AsNoTracking()
                            on d.UserId equals u.Id into uj
                        from u in uj.DefaultIfEmpty()
                        where (dep != null && dep.BranchId == bid)
                           || (dep == null && u != null && u.BranchId == bid)
                        select d;
                }

                // apply date range on documents if provided (CreatedAt is DateTime)
                if (start.HasValue || endEx.HasValue)
                {
                    var startUtc = start?.UtcDateTime;
                    var endUtc = endEx?.UtcDateTime;

                    if (startUtc.HasValue)
                        docsQ = docsQ.Where(d => d.CreatedAt >= startUtc.Value);
                    if (endUtc.HasValue)
                        docsQ = docsQ.Where(d => d.CreatedAt < endUtc.Value);
                }

                var documentsCount = await docsQ.LongCountAsync();

                // ====== Groups (إذا ما عندك CreatedAt للمجموعات، ما فينا نفلترها زمنياً بشكل صحيح) ======
                var groupsQ = from g in _context.ArchiveGroups.AsNoTracking()
                              join dep in _context.Departments.AsNoTracking()
                                  on g.DepartmentId equals dep.DepartmentId
                              select new { g, dep.BranchId, dep.DepartmentId };

                // scope
                if (!s.IsSuperAdmin)
                {
                    if (s.IsBranchManager && s.BranchId.HasValue)
                        groupsQ = groupsQ.Where(x => x.BranchId == s.BranchId.Value);
                    else if (s.DepartmentId.HasValue)
                        groupsQ = groupsQ.Where(x => x.DepartmentId == s.DepartmentId.Value);
                    else
                        groupsQ = groupsQ.Where(x => false);
                }

                if (f.DepartmentId.HasValue) groupsQ = groupsQ.Where(x => x.DepartmentId == f.DepartmentId.Value);
                if (f.BranchId.HasValue) groupsQ = groupsQ.Where(x => x.BranchId == f.BranchId.Value);

                var groupsCount = await groupsQ.LongCountAsync();

                // ====== Audit (scoped + date range optional) ======
                var auditQ = ApplyScopeAudit(_context.AuditLogs.AsNoTracking(), s);

                if (start.HasValue || endEx.HasValue)
                    auditQ = ApplyRange(auditQ, start, endEx, a => a.Timestamp);

                // Recent
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

                // Titles (ضمن نفس docs scope)
                var docIds = recent.Where(r => r.DocumentId.HasValue).Select(r => r.DocumentId!.Value).Distinct().ToList();
                if (docIds.Count > 0)
                {
                    var titles = await docsQ
                        .Where(d => docIds.Contains(d.DocumentId))
                        .Select(d => new { d.DocumentId, d.Title })
                        .ToDictionaryAsync(x => x.DocumentId, x => x.Title);

                    foreach (var r in recent.Where(x => x.DocumentId.HasValue))
                        if (titles.TryGetValue(r.DocumentId!.Value, out var t)) r.DocumentTitle = t;
                }

                // Top Users
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

                // ====== Branches Stats (مرئية حسب الدور) ======
                IQueryable<Branch> branchesVisibleQ = _context.Branchs.AsNoTracking();

                if (!s.IsSuperAdmin)
                {
                    if (s.IsBranchManager && s.BranchId.HasValue)
                        branchesVisibleQ = branchesVisibleQ.Where(b => b.BranchId == s.BranchId.Value);
                    else
                    {
                        // موظف: فرع قسمه (إذا عنده قسم)
                        if (s.DepartmentId.HasValue)
                        {
                            var deptBranchId = await _context.Departments.AsNoTracking()
                                .Where(d => d.DepartmentId == s.DepartmentId.Value)
                                .Select(d => (long?)d.BranchId)
                                .FirstOrDefaultAsync();

                            if (deptBranchId.HasValue)
                                branchesVisibleQ = branchesVisibleQ.Where(b => b.BranchId == deptBranchId.Value);
                            else
                                branchesVisibleQ = branchesVisibleQ.Where(b => false);
                        }
                        else branchesVisibleQ = branchesVisibleQ.Where(b => false);
                    }
                }

                if (f.BranchId.HasValue)
                    branchesVisibleQ = branchesVisibleQ.Where(b => b.BranchId == f.BranchId.Value);

                var branchesStats = await (
                    from b in branchesVisibleQ
                    join dep in _context.Departments.AsNoTracking() on b.BranchId equals dep.BranchId
                    join d in docsQ on dep.DepartmentId equals d.DepartmentId into dg
                    select new BranchStatsDto
                    {
                        BranchId = b.BranchId,
                        BranchName = b.BranchName,
                        DocumentsCount = dg.LongCount()
                    })
                    .OrderByDescending(x => x.DocumentsCount)
                    .ToListAsync();

                // ====== Departments Stats ======
                var departmentsVisibleQ = _context.Departments.AsNoTracking().AsQueryable();

                if (!s.IsSuperAdmin)
                {
                    if (s.IsBranchManager && s.BranchId.HasValue)
                        departmentsVisibleQ = departmentsVisibleQ.Where(d => d.BranchId == s.BranchId.Value);
                    else if (s.DepartmentId.HasValue)
                        departmentsVisibleQ = departmentsVisibleQ.Where(d => d.DepartmentId == s.DepartmentId.Value);
                    else
                        departmentsVisibleQ = departmentsVisibleQ.Where(d => false);
                }

                if (f.BranchId.HasValue)
                    departmentsVisibleQ = departmentsVisibleQ.Where(d => d.BranchId == f.BranchId.Value);

                if (f.DepartmentId.HasValue)
                    departmentsVisibleQ = departmentsVisibleQ.Where(d => d.DepartmentId == f.DepartmentId.Value);

                var departmentsStats = await (
                    from dep in departmentsVisibleQ
                    join d in docsQ on dep.DepartmentId equals d.DepartmentId into dg
                    select new DepartmentStatsDto
                    {
                        DepartmentId = dep.DepartmentId,
                        DepartmentName = dep.DepartmentName,
                        DocumentsCount = dg.LongCount()
                    })
                    .OrderByDescending(x => x.DocumentsCount)
                    .ToListAsync();

                // ====== Growth (إذا ما في range بنستخدم GrowthMonths كـ default) ======
                // إذا المستخدم ما حدد from/to: منطبق GrowthMonths فقط على الـ growth chart وليس على باقي الإحصائيات.
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

                var growthRaw = await (from d in ApplyScopeDocs(_context.Documents.AsNoTracking(), s)
                                       where d.CreatedAt >= growthFromUtc && d.CreatedAt < growthToUtc
                                       group d by new { d.CreatedAt.Year, d.CreatedAt.Month } into g
                                       orderby g.Key.Year, g.Key.Month
                                       select new
                                       {
                                           g.Key.Year,
                                           g.Key.Month,
                                           Count = g.LongCount()
                                       })
                    .ToListAsync();

                var growth = growthRaw.Select(x => new DocumentGrowthPointDto
                {
                    Period = $"{x.Year:D4}-{x.Month:D2}",
                    Count = x.Count
                }).ToList();

                growth = FillMissingMonths(growth, growthFromUtc, growthToUtc);

                // ====== Storage Usage (ضمن docsQ => إذا في range رح ينحصر تلقائياً) ======
                var attScoped = from a in _context.Attachments.AsNoTracking()
                                join d in docsQ on a.OwnerId equals d.DocumentId
                                where a.OwnerType == "Document"
                                select new { a, d.DocumentTypeId };

                var totalBytes = await attScoped.SumAsync(x => (long?)x.a.FileSize) ?? 0;

                var byType = await (from x in attScoped
                                    join t in _context.DocumentTypes.AsNoTracking()
                                        on x.DocumentTypeId equals t.DocumentTypeId
                                    group new { x, t } by new { t.DocumentTypeId, t.DocumentTypeName } into g
                                    select new StorageUsageByTypeDto
                                    {
                                        DocumentTypeId = g.Key.DocumentTypeId,
                                        DocumentTypeName = g.Key.DocumentTypeName,
                                        SizeBytes = g.Sum(y => (long?)y.x.a.FileSize) ?? 0,
                                        DocumentsCount = g.Select(y => y.x.a.OwnerId).Distinct().LongCount()
                                    })
                    .OrderByDescending(x => x.SizeBytes)
                    .ToListAsync();

                foreach (var bt in byType)
                    bt.SizeReadable = FormatBytes(bt.SizeBytes);

                var storage = new StorageUsageDto
                {
                    TotalBytes = totalBytes,
                    TotalReadable = FormatBytes(totalBytes),
                    ByType = byType
                };

                // ====== Top Document Types (ضمن docsQ => ضمن الفترة) ======
                var topTypes = await (from d in docsQ
                                      join t in _context.DocumentTypes.AsNoTracking()
                                          on d.DocumentTypeId equals t.DocumentTypeId
                                      group new { d, t } by new { t.DocumentTypeId, t.DocumentTypeName } into g
                                      select new TopDocumentTypeDto
                                      {
                                          DocumentTypeId = g.Key.DocumentTypeId,
                                          DocumentTypeName = g.Key.DocumentTypeName,
                                          DocumentsCount = g.LongCount()
                                      })
                    .OrderByDescending(x => x.DocumentsCount)
                    .Take(10)
                    .ToListAsync();

                var sizeByType = byType.ToDictionary(x => x.DocumentTypeId, x => (long?)x.SizeBytes);
                foreach (var t in topTypes)
                    t.TotalSizeBytes = sizeByType.TryGetValue(t.DocumentTypeId, out var sz) ? sz : 0;

                var dto = new DashboardStatsDto
                {
                    DocumentsCount = documentsCount,
                    GroupsCount = groupsCount,
                    RecentActivities = recent,
                    MostActiveUsers = topUsers,
                    BranchesStats = branchesStats,
                    DepartmentsStats = departmentsStats,
                    DocumentGrowth = growth,
                    StorageUsage = storage,
                    TopDocumentTypes = topTypes
                };

                // إذا بدك تعرض periodText بالـ API ممكن تضيفه للـ DTO أو تحطه warnings… تركته كما هو.
                return Result<DashboardStatsDto>.Success(dto);
            }
            catch (Exception ex)
            {
                return Result<DashboardStatsDto>.Failure(new[] { $"Error building dashboard: {ex.Message}" });
            }
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
