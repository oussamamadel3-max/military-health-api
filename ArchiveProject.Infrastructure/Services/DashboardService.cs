using ArchiveProject.Application.Common.Interfaces.DashboardInterfaces;
using ArchiveProject.Application.Common.Interfaces.UserContextInterfaces;
using ArchiveProject.Application.Common.Results;
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

        // نطاق الوثائق (سوبر: الكل، مدير فرع: وثائق أقسام الفرع، موظف: قسمه أو وثائقه)
        private IQueryable<Domain.Entities.Document> ApplyScopeDocs(IQueryable<Domain.Entities.Document> q, Scope s)
        {
            if (s.IsSuperAdmin) return q;

            var joined = from d in q
                         join dep in _context.Departments.AsNoTracking()
                             on d.DepartmentId equals dep.DepartmentId
                         select new { d, dep.BranchId };

            if (s.IsBranchManager && s.BranchId.HasValue)
                return joined.Where(x => x.BranchId == s.BranchId.Value).Select(x => x.d);

            if (s.DepartmentId.HasValue)
                return joined.Where(x => x.d.DepartmentId == s.DepartmentId.Value || x.d.UserId == s.UserId)
                             .Select(x => x.d);

            return joined.Where(x => x.d.UserId == s.UserId).Select(x => x.d);
        }

        // نطاق السجلات (Audit) بنفس المنطق
        private IQueryable<Domain.Entities.AuditLog> ApplyScopeAudit(IQueryable<Domain.Entities.AuditLog> q, Scope s)
        {
            if (s.IsSuperAdmin) return q;

            var joined = from a in q
                         join u in _context.Users.AsNoTracking() on a.UserId equals u.Id into uu
                         from u in uu.DefaultIfEmpty()
                         select new
                         {
                             a,
                             BranchId = (long?)(u == null ? null : u.BranchId),
                             DepartmentId = (long?)(u == null ? null : u.DepartmentId)
                         };

            if (s.IsBranchManager && s.BranchId.HasValue)
                return joined.Where(x => x.BranchId == s.BranchId.Value).Select(x => x.a);

            if (s.DepartmentId.HasValue)
                return joined.Where(x => x.DepartmentId == s.DepartmentId.Value || x.a.UserId == s.UserId)
                             .Select(x => x.a);

            return joined.Where(x => x.a.UserId == s.UserId).Select(x => x.a);
        }

        public async Task<Result<DashboardStatsDto>> GetAsync(DashboardFilter f)
        {
            try
            {
                var s = await GetScopeAsync();

                // حدود التاريخ الافتراضية
                var toUtc = (f.To ?? DateTime.UtcNow).Date.AddDays(1);
                var fromUtc = (f.From ?? DateTime.UtcNow.AddMonths(-Math.Abs(f.GrowthMonths))).Date;

                // ====== Documents Query (scoped) ======
                var docsQ = ApplyScopeDocs(_context.Documents.AsNoTracking(), s);

                // فلترة إضافية اختيارية (لكن ضمن صلاحيات scope)
                if (f.DepartmentId.HasValue)
                    docsQ = docsQ.Where(d => d.DepartmentId == f.DepartmentId.Value);

                if (f.BranchId.HasValue)
                {
                    // فلترة فرع (من خلال الأقسام)
                    docsQ = from d in docsQ
                            join dep in _context.Departments.AsNoTracking()
                                on d.DepartmentId equals dep.DepartmentId
                            where dep.BranchId == f.BranchId.Value
                            select d;
                }

                // ====== Groups Count ======
                var groupsQ = from g in _context.ArchiveGroups.AsNoTracking()
                              join dep in _context.Departments.AsNoTracking()
                                  on g.DepartmentId equals dep.DepartmentId
                              select new { g, dep.BranchId, dep.DepartmentId };

                // طبق نفس منطق النطاق على المجموعات
                if (!s.IsSuperAdmin)
                {
                    if (s.IsBranchManager && s.BranchId.HasValue)
                        groupsQ = groupsQ.Where(x => x.BranchId == s.BranchId.Value);
                    else if (s.DepartmentId.HasValue)
                        groupsQ = groupsQ.Where(x => x.DepartmentId == s.DepartmentId.Value);
                    else
                        groupsQ = groupsQ.Where(x => false);
                }

                // فلاتر إضافية اختيارية
                if (f.DepartmentId.HasValue) groupsQ = groupsQ.Where(x => x.DepartmentId == f.DepartmentId.Value);
                if (f.BranchId.HasValue) groupsQ = groupsQ.Where(x => x.BranchId == f.BranchId.Value);

                var documentsCount = await docsQ.LongCountAsync();
                var groupsCount = await groupsQ.LongCountAsync();

                // ====== Recent Activities ======
                var auditQ = ApplyScopeAudit(_context.AuditLogs.AsNoTracking(), s)
                    .Where(a => a.Timestamp >= fromUtc && a.Timestamp < toUtc);

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

                // عناوين المستندات للأنشطة
                var docIdsInRecent = recent.Where(r => r.DocumentId.HasValue)
                                           .Select(r => r.DocumentId!.Value)
                                           .Distinct()
                                           .ToList();

                if (docIdsInRecent.Count > 0)
                {
                    var titles = await ApplyScopeDocs(_context.Documents.AsNoTracking(), s)
                        .Where(d => docIdsInRecent.Contains(d.DocumentId))
                        .Select(d => new { d.DocumentId, d.Title })
                        .ToDictionaryAsync(x => x.DocumentId, x => x.Title);

                    foreach (var r in recent.Where(x => x.DocumentId.HasValue))
                        if (titles.TryGetValue(r.DocumentId!.Value, out var t)) r.DocumentTitle = t;
                }

                // ====== Most Active Users ======
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

                // ====== Branches Stats (فروع فعليًا) ======
                // نعرض فقط الفروع ضمن النطاق (سوبر: الكل، مدير فرع: فرعه، موظف: فرع قسمه)
                IQueryable<Branch> branchesVisibleQ = _context.Branchs.AsNoTracking();

                if (!s.IsSuperAdmin)
                {
                    if (s.IsBranchManager && s.BranchId.HasValue)
                        branchesVisibleQ = branchesVisibleQ.Where(b => b.BranchId == s.BranchId.Value);
                    else if (s.DepartmentId.HasValue)
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
                    else
                        branchesVisibleQ = branchesVisibleQ.Where(b => false);
                }

                // فلتر branchId (اختياري) لكن ضمن المرئي
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

                // ====== Departments Stats (الأقسام) ======
                // الأقسام المرئية حسب النطاق
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

                // فلاتر إضافية
                if (f.BranchId.HasValue)
                    departmentsVisibleQ = departmentsVisibleQ.Where(d => d.BranchId == f.BranchId.Value);

                if (f.DepartmentId.HasValue)
                    departmentsVisibleQ = departmentsVisibleQ.Where(d => d.DepartmentId == f.DepartmentId.Value);

                // left join لإظهار القسم حتى لو 0 وثائق
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

                // ====== Document Growth ======
                var growthRaw = await (from d in docsQ
                                       where d.CreatedAt >= fromUtc && d.CreatedAt < toUtc
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

                growth = FillMissingMonths(growth, fromUtc, toUtc);

                // ====== Storage Usage ======
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

                // ====== Top Document Types ======
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

                // ====== Result ======
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
