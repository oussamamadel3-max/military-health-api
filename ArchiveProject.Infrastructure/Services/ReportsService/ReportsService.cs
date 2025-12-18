using ArchiveProject.Application.Common.Interfaces;
using ArchiveProject.Application.Common.Interfaces.DashboardInterfaces;
using ArchiveProject.Application.Common.Interfaces.Reports;
using ArchiveProject.Application.Common.Interfaces.UserContextInterfaces;
using ArchiveProject.Application.Dtos.DashboardDtos;
using ArchiveProject.Application.Dtos.DocumentDtos;
using ArchiveProject.Application.Dtos.ExportDtos;
using ArchiveProject.Application.Dtos.ReportDtos;
using ArchiveProject.Application.ModelsDto.AuditLogDtos;
using ArchiveProject.Domain.Entities;
using ArchiveProject.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArchiveProject.Infrastructure.Services.Reports
{
    public class ReportsService : IReportsService
    {
        private readonly MainDbContext _context;
        private readonly IUserContextService _userCtx;
        private readonly IExportService _export;
        private readonly IDashboardService _dashboard;

        public ReportsService(MainDbContext context, IUserContextService userCtx, IExportService export, IDashboardService dashboard)
        {
            _context = context;
            _userCtx = userCtx;
            _export = export;
            _dashboard = dashboard;
        }

        private sealed record Scope(bool IsSuper, bool IsBranchMgr, long? BranchId, long? DepartmentId, string UserId);

        private async Task<Scope> GetScopeAsync()
        {
            var userId = _userCtx.UserId ?? "";

            var roles = await _context.UserRoleAssociations
                .Where(x => x.UserId == userId)
                .Join(_context.Roles, a => a.RoleId, r => r.Id, (a, r) => r.NormalizedName)
                .ToListAsync();

            bool isSuper = roles.Any(r => r == "SUPERADMIN");
            bool isMgr = roles.Any(r => r == "BRANCHMANAGER");

            var actor = await _context.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.BranchId, u.DepartmentId })
                .FirstOrDefaultAsync();

            return new Scope(isSuper, isMgr, actor?.BranchId, actor?.DepartmentId, userId);
        }

        private IQueryable<Domain.Entities.Document> ApplyScopeDocs(IQueryable<Domain.Entities.Document> q, Scope s)
        {
            if (s.IsSuper) return q;

            var joined = from d in q
                         join dep in _context.Departments.AsNoTracking() on d.DepartmentId equals dep.DepartmentId
                         select new { d, dep.BranchId };

            if (s.IsBranchMgr && s.BranchId.HasValue)
                return joined.Where(x => x.BranchId == s.BranchId.Value).Select(x => x.d);

            if (s.DepartmentId.HasValue)
                return joined.Where(x => x.d.DepartmentId == s.DepartmentId.Value).Select(x => x.d);

            return joined.Where(x => x.d.UserId == s.UserId).Select(x => x.d);
        }

        #region Export Documents
        public async Task<Result<ExportFileDto>> ExportDocumentsAsync(DocumentSearchCriteria c, string format)
        {
            try
            {
                var s = await GetScopeAsync();

                var q = ApplyScopeDocs(_context.Documents.AsNoTracking(), s);

                // Filters
                if (c.DepartmentId.HasValue) q = q.Where(d => d.DepartmentId == c.DepartmentId.Value);
                if (c.DocumentTypeId.HasValue) q = q.Where(d => d.DocumentTypeId == c.DocumentTypeId.Value);
                if (c.ArchiveGroupId.HasValue) q = q.Where(d => d.ArchiveGroupId == c.ArchiveGroupId.Value);
                if (!string.IsNullOrWhiteSpace(c.Title)) q = q.Where(d => d.Title.Contains(c.Title));
                if (!string.IsNullOrWhiteSpace(c.Description)) q = q.Where(d => d.Description.Contains(c.Description));
                if (c.CreatedFrom.HasValue) q = q.Where(d => d.CreatedAt >= c.CreatedFrom.Value);
                if (c.CreatedFrom.HasValue)
                {
                    var from = c.CreatedFrom.Value.Date;
                    q = q.Where(d => d.CreatedAt >= from);
                }

                if (c.CreatedTo.HasValue)
                {
                    var toExclusive = c.CreatedTo.Value.Date.AddDays(1);
                    q = q.Where(d => d.CreatedAt < toExclusive);
                }

                // Order newest first
                q = q.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.DocumentId);

                // Projection (مع أسماء الفرع/القسم/النوع)
                var rows = await (from d in q
                                  join dep in _context.Departments.AsNoTracking() on d.DepartmentId equals dep.DepartmentId
                                  join br in _context.Branchs.AsNoTracking() on dep.BranchId equals br.BranchId
                                  join dt in _context.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals dt.DocumentTypeId into dtt
                                  from dt in dtt.DefaultIfEmpty()
                                  join ag in _context.ArchiveGroups.AsNoTracking() on d.ArchiveGroupId equals ag.ArchiveGroupId into agg
                                  from ag in agg.DefaultIfEmpty()
                                  select new DocumentReportRowDto
                                  {
                                      DocumentNumber = d.DocumentNumber,
                                      Title = d.Title ?? "",
                                      BranchName = br.BranchName ?? "",
                                      DepartmentName = dep.DepartmentName ?? "",
                                      DocumentTypeName = dt != null ? dt.DocumentTypeName : "",
                                      ArchiveGroupTitle = ag != null ? ag.Title : null,
                                      CreatedAt = d.CreatedAt,
                                      CreatedBy = d.User.UserName ?? ""
                                  }).ToListAsync();

                var deptName = c.DepartmentId.HasValue
                    ? await _context.Departments.Where(x => x.DepartmentId == c.DepartmentId.Value).Select(x => x.DepartmentName).FirstOrDefaultAsync()
                    : null;

                var branchName = (s.IsBranchMgr && s.BranchId.HasValue)
                    ? await _context.Branchs.Where(x => x.BranchId == s.BranchId.Value).Select(x => x.BranchName).FirstOrDefaultAsync()
                    : null;

                string? periodText = null;
                if (c.CreatedFrom.HasValue || c.CreatedTo.HasValue)
                {
                    var fromTxt = c.CreatedFrom?.ToString("yyyy/MM/dd") ?? "غير محدد";
                    var toTxt = c.CreatedTo?.ToString("yyyy/MM/dd") ?? "غير محدد";
                    periodText = $"{fromTxt} → {toTxt}";
                }

                var opt = new ExportOptions
                {
                    ReportName = "Documents_Report",
                    SheetName = "Documents",
                    CustomTitle = "تقرير الوثائق",
                    BranchName = branchName,
                    DepartmentName = deptName,
                    PeriodText = periodText,
                    IncludeLogo = true,   // ✅ أضفها
                    IncludeFooter = true, // (إذا بدك)
                    ColumnMappings = new Dictionary<string, string>
                    {
                        ["DocumentNumber"] = "رقم الوثيقة",
                        ["Title"] = "العنوان",
                        ["BranchName"] = "الفرع",
                        ["DepartmentName"] = "القسم",
                        ["DocumentTypeName"] = "نوع الوثيقة",
                        ["ArchiveGroupTitle"] = "مجموعة الأرشفة",
                        ["CreatedAt"] = "تاريخ الإنشاء",
                        ["CreatedBy"] = "أنشئ بواسطة",
                    }
                };

                ExportFileDto file = format.ToLower() == "pdf"
                    ? await _export.ExportToPdfAsync(rows, opt)
                    : await _export.ExportToExcelAsync(rows, opt);

                return Result<ExportFileDto>.Success(file);
            }
            catch (Exception ex)
            {
                return Result<ExportFileDto>.Failure(new[] { $"خطأ بتصدير تقرير الوثائق: {ex.Message}" });
            }
        }
        #endregion

        #region Export Audit Logs
        public async Task<Result<ExportFileDto>> ExportAuditLogsAsync(AuditLogFilter filter, string format)
        {
            try
            {
                filter ??= new AuditLogFilter();

                var s = await GetScopeAsync();

                var q = _context.AuditLogs.AsNoTracking().AsQueryable();

                // ====== Apply Scope ======
                if (!s.IsSuper)
                {
                    var joined = from a in q
                                 join u in _context.Users.AsNoTracking() on a.UserId equals u.Id into uu
                                 from u in uu.DefaultIfEmpty()
                                 select new { a, u };

                    if (s.IsBranchMgr && s.BranchId.HasValue)
                        q = joined.Where(x => x.u != null && x.u.BranchId == s.BranchId.Value).Select(x => x.a);
                    else if (s.DepartmentId.HasValue)
                        q = joined.Where(x => x.u != null && x.u.DepartmentId == s.DepartmentId.Value).Select(x => x.a);
                    else
                        q = joined.Where(x => x.a.UserId == s.UserId).Select(x => x.a);
                }

                // ====== Filters (نفس GetAllFiltered) ======

                if (!string.IsNullOrWhiteSpace(filter.searchUserName))
                {
                    var ns = filter.searchUserName.Trim().ToLower();
                    q = q.Where(a =>
                        (a.UserName ?? "").ToLower().Contains(ns));
                }

                if (!string.IsNullOrWhiteSpace(filter.userName))
                {
                    var name = filter.userName.Trim().ToLower();
                    q = q.Where(a =>
                        (a.UserName ?? "").ToLower().Contains(name) ||
                        (a.UserId ?? "").ToLower().Contains(name));
                }

                if (filter.actionType.HasValue)
                {
                    var at = filter.actionType.Value.ToString();
                    q = q.Where(a => a.ActionType == at);
                }

                if (filter.tableNames != null && filter.tableNames.Count > 0)
                {
                    var set = filter.tableNames
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => t.Trim().ToLower())
                        .Distinct()
                        .ToList();

                    if (set.Count > 0)
                        q = q.Where(a => set.Contains((a.TableName ?? "").ToLower()));
                }

                // Date Range (From/To)
                ApplyDateRangeForExport(ref q, filter.From, filter.To, out var periodText);

                q = q.OrderByDescending(a => a.Timestamp);

                var rows = await q.Select(a => new AuditLogReportRowDto
                {
                    Timestamp = a.Timestamp,
                    UserName = a.UserName ?? "",
                    ActionType = a.ActionType ?? "",
                    TableName = a.TableName ?? "",
                    DocumentId = a.RecordId
                }).ToListAsync();

                var opt = new ExportOptions
                {
                    ReportName = "AuditLogs_Report",
                    SheetName = "AuditLogs",
                    CustomTitle = "تقرير سجل التدقيق",
                    PeriodText = periodText,
                    IncludeLogo = true,   // ✅ أضفها
                    IncludeFooter = true, // (إذا بدك)
                    ColumnMappings = new Dictionary<string, string>
                    {
                        ["Timestamp"] = "التاريخ",
                        ["UserName"] = "المستخدم",
                        ["ActionType"] = "الإجراء",
                        ["TableName"] = "اسم الجدول",
                        ["DocumentId"] = "رقم السطر",
                    }
                };

                ExportFileDto file = format.ToLower() == "pdf"
                    ? await _export.ExportToPdfAsync(rows, opt)
                    : await _export.ExportToExcelAsync(rows, opt);

                return Result<ExportFileDto>.Success(file);
            }
            catch (Exception ex)
            {
                return Result<ExportFileDto>.Failure(new[] { $"خطأ بتصدير تقرير التدقيق: {ex.Message}" });
            }
        }

        // ===== Helper: Date Range + PeriodText =====
        private static void ApplyDateRangeForExport(ref IQueryable<AuditLog> q,DateTimeOffset? from,DateTimeOffset? to,out string? periodText)
        {
            periodText = null;
            if (!from.HasValue && !to.HasValue) return;

            // إذا بدك تعتبر "to" شامل لنهاية اليوم عند إدخال تاريخ بدون وقت:
            DateTimeOffset? start = null;
            DateTimeOffset? endExclusive = null;

            if (from.HasValue)
            {
                start = from.Value;
            }

            if (to.HasValue)
            {
                var t = to.Value;

                // إذا الوقت 00:00:00 اعتبره تاريخ فقط => نهاية اليوم (exclusive) = +1 day
                endExclusive = (t.TimeOfDay == TimeSpan.Zero) ? t.AddDays(1) : t;
            }

            if (start.HasValue) q = q.Where(a => a.Timestamp >= start.Value);
            if (endExclusive.HasValue) q = q.Where(a => a.Timestamp < endExclusive.Value);

            var fromText = from.HasValue ? from.Value.ToString("yyyy/MM/dd") : "—";
            var toText = to.HasValue ? to.Value.ToString("yyyy/MM/dd") : "—";
            periodText = $"{fromText} → {toText}";
        }

        #endregion

        #region Export Dashboard
        //public async Task<Result<ExportFileDto>> ExportDashboardAsync(DashboardFilter filter, string format)
        //{
        //    try
        //    {
        //        var res = await _dashboard.GetAsync(filter);
        //        if (!res.Succeeded || res.Value == null)
        //            return Result<ExportFileDto>.Failure(res.Errors);

        //        // نحول الـ DashboardStatsDto لصفوف بسيطة للتصدير (أو أكثر من شيت لاحقًا)
        //        var rows = new List<DashboardExportRow>
        //        {
        //            new() { Name = "عدد الوثائق", Value = (res.Value.DocumentsCount).ToString() },
        //            new() { Name = "عدد المجموعات", Value = (res.Value.GroupsCount).ToString() },
        //            new() { Name = "إجمالي التخزين", Value = res.Value.StorageUsage?.TotalReadable ?? "0 B" },
        //            new() { Name = "عدد الأنشطة الأخيرة", Value = (res.Value.RecentActivities?.Count ?? 0).ToString() }
        //        };

        //        var opt = new ExportOptions
        //        {
        //            ReportName = "Dashboard_Report",
        //            SheetName = "Dashboard",
        //            CustomTitle = "تقرير لوحة التحكم",
        //            ColumnMappings = new Dictionary<string, string>
        //            {
        //                ["Name"] = "البند",
        //                ["Value"] = "القيمة"
        //            }
        //        };

        //        ExportFileDto file = format.ToLower() == "pdf"
        //            ? await _export.ExportToPdfAsync(rows, opt)
        //            : await _export.ExportToExcelAsync(rows, opt);

        //        return Result<ExportFileDto>.Success(file);
        //    }
        //    catch (Exception ex)
        //    {
        //        return Result<ExportFileDto>.Failure(new[] { $"خطأ بتصدير تقرير الداشبورد: {ex.Message}" });
        //    }
        //}
        public async Task<Result<ExportFileDto>> ExportDashboardAsync(DashboardFilter filter, string format)
        {
            try
            {
                var res = await _dashboard.GetAsync(filter);
                if (!res.Succeeded || res.Value == null)
                    return Result<ExportFileDto>.Failure(res.Errors);

                var dto = res.Value;

                // period text (إذا ما حددت تاريخين نخليه null)
                string? periodText = null;
                if (filter.From.HasValue || filter.To.HasValue)
                {
                    var fromTxt = filter.From.HasValue ? filter.From.Value.ToString("yyyy/MM/dd") : "—";
                    var toTxt = filter.To.HasValue ? filter.To.Value.ToString("yyyy/MM/dd") : "—";
                    periodText = $"{fromTxt} → {toTxt}";
                }

                var rows = new List<DashboardExportRow>
                {
                    new() { Name = "عدد الوثائق", Value = dto.DocumentsCount.ToString() },
                    new() { Name = "عدد المجموعات", Value = dto.GroupsCount.ToString() },
                    new() { Name = "إجمالي التخزين", Value = dto.StorageUsage?.TotalReadable ?? "0 B" },

                    // فاصل/عنوان قسم
                    new() { Name = "", Value = "" },
                    new() { Name = "أكثر أنواع الوثائق استخداماً", Value = "" },
                };

                // Top Document Types (مثلاً أعلى 5)
                if (dto.TopDocumentTypes != null && dto.TopDocumentTypes.Count > 0)
                {
                    foreach (var t in dto.TopDocumentTypes.Take(5))
                    {
                        var size = t.TotalSizeBytes.HasValue ? $" | الحجم: {FormatBytes(t.TotalSizeBytes.Value)}" : "";
                        rows.Add(new DashboardExportRow
                        {
                            Name = t.DocumentTypeName,
                            Value = $"{t.DocumentsCount} وثيقة{size}"
                        });
                    }
                }
                else
                {
                    rows.Add(new DashboardExportRow { Name = "لا يوجد بيانات", Value = "" });
                }

                // فاصل/عنوان قسم
                rows.Add(new DashboardExportRow { Name = "", Value = "" });
                rows.Add(new DashboardExportRow { Name = "أكثر المستخدمين نشاطًا", Value = "" });

                // Most Active Users (مثلاً أعلى 5)
                if (dto.MostActiveUsers != null && dto.MostActiveUsers.Count > 0)
                {
                    foreach (var u in dto.MostActiveUsers.Take(5))
                    {
                        rows.Add(new DashboardExportRow
                        {
                            Name = u.UserName,
                            Value = $"{u.ActionsCount} إجراء | آخر نشاط: {u.LastActivityAt}"
                        });
                    }
                }
                else
                {
                    rows.Add(new DashboardExportRow { Name = "لا يوجد بيانات", Value = "" });
                }

                var opt = new ExportOptions
                {
                    ReportName = "Dashboard_Report",
                    SheetName = "Dashboard",
                    CustomTitle = "تقرير لوحة التحكم",
                    PeriodText = periodText,
                    IncludeLogo = true, // إذا بدك يظهر اللوغو
                    ColumnMappings = new Dictionary<string, string>
                    {
                        ["Name"] = "البند",
                        ["Value"] = "القيمة"
                    }
                };

                ExportFileDto file = format.ToLower() == "pdf"
                    ? await _export.ExportToPdfAsync(rows, opt)
                    : await _export.ExportToExcelAsync(rows, opt);

                return Result<ExportFileDto>.Success(file);
            }
            catch (Exception ex)
            {
                return Result<ExportFileDto>.Failure(new[] { $"خطأ بتصدير تقرير الداشبورد: {ex.Message}" });
            }
        }

        // إذا عندك FormatBytes موجودة بدashboard service تقدر تنسخها هون، أو تعملها private داخل ReportsService:
        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double val = bytes;
            int i = 0;
            while (val >= 1024 && i < units.Length - 1) { val /= 1024; i++; }
            return $"{val:0.##} {units[i]}";
        }
        #endregion

    }
}
