using ArchiveProject.Application.Common.Interfaces;
using ArchiveProject.Application.Common.Utilities;
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

        private sealed record Scope(bool IsSuper, bool IsBranchMgr, string UserId);

        private async Task<Scope> GetScopeAsync()
        {
            var userId = _userCtx.UserId ?? "";

            var roles = await _context.UserRoleAssociations
                .Where(x => x.UserId == userId)
                .Join(_context.Roles, a => a.RoleId, r => r.Id, (a, r) => r.NormalizedName)
                .ToListAsync();

            bool isSuper = roles.Any(r => r == "SUPERADMIN");
            bool isMgr = roles.Any(r => r == "BRANCHMANAGER");

            return new Scope(isSuper, isMgr, userId);
        }

        private IQueryable<Domain.Entities.Document> ApplyScopeDocs(IQueryable<Domain.Entities.Document> q, Scope s)
        {
            if (s.IsSuper || s.IsBranchMgr) return q;
            return q.Where(d => d.UserId == s.UserId);
        }

        #region Export Documents
        public async Task<Result<ExportFileDto>> ExportDocumentsAsync(DocumentSearchCriteria c, string format)
        {
            try
            {
                var s = await GetScopeAsync();

                var q = ApplyScopeDocs(_context.Documents.AsNoTracking(), s);

                // Filters
                if (!string.IsNullOrWhiteSpace(c.Title))
                {
                    var titleEsc = SqlLikePatternEscape.EscapeForSqlServerLike(c.Title.Trim());
                    q = q.Where(d => d.Title.Contains(titleEsc));
                }
                if (!string.IsNullOrWhiteSpace(c.Description))
                {
                    var descEsc = SqlLikePatternEscape.EscapeForSqlServerLike(c.Description.Trim());
                    q = q.Where(d => d.Description.Contains(descEsc));
                }
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

                var rows = await q.Select(d => new DocumentReportRowDto
                {
                    DocumentNumber = d.DocumentNumber,
                    Title = d.Title ?? "",
                    CreatedAt = d.CreatedAt,
                    CreatedBy = d.User.UserName ?? ""
                }).ToListAsync();

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
                    PeriodText = periodText,
                    IncludeLogo = true,   // ✅ أضفها
                    IncludeFooter = true, // (إذا بدك)
                    ColumnMappings = new Dictionary<string, string>
                    {
                        ["DocumentNumber"] = "رقم الوثيقة",
                        ["Title"] = "العنوان",
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
                if (!s.IsSuper && !s.IsBranchMgr)
                    q = q.Where(a => a.UserId == s.UserId);

                // ====== Filters (نفس GetAllFiltered) ======

                if (!string.IsNullOrWhiteSpace(filter.searchUserName))
                {
                    var ns = SqlLikePatternEscape.EscapeForSqlServerLike(filter.searchUserName.Trim().ToLower());
                    q = q.Where(a =>
                        (a.UserName ?? "").ToLower().Contains(ns));
                }

                if (!string.IsNullOrWhiteSpace(filter.userName))
                {
                    var name = SqlLikePatternEscape.EscapeForSqlServerLike(filter.userName.Trim().ToLower());
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
                    new() { Name = "إجمالي التخزين", Value = dto.StorageUsage?.TotalReadable ?? "0 B" },

                    new() { Name = "", Value = "" },
                    new() { Name = "أكثر المستخدمين نشاطًا", Value = "" },
                };

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
        #endregion
    }
}
