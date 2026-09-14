using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Common.Utilities;
using Application.Dtos.DashboardDtos;
using Application.Dtos.DocumentDtos;
using Application.Dtos.ExportDtos;
using Application.Dtos.ReportDtos;
using Application.Exceptions;
using Application.ModelsDto.AuditLogDtos;
using Domain.Entities;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Reports
{
    public class ReportsService : IReportsService
    {
        private readonly IDocumentRepository _documentRepository;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IActorScopeProvider _actorScopeProvider;
        private readonly IUserContextService _userCtx;
        private readonly IExportService _export;
        private readonly IDashboardService _dashboard;
        private readonly IMapper _mapper;

        public ReportsService(
            IDocumentRepository documentRepository,
            IAuditLogRepository auditLogRepository,
            IActorScopeProvider actorScopeProvider,
            IUserContextService userCtx,
            IExportService export,
            IDashboardService dashboard,
            IMapper mapper)
        {
            _documentRepository = documentRepository;
            _auditLogRepository = auditLogRepository;
            _actorScopeProvider = actorScopeProvider;
            _userCtx = userCtx;
            _export = export;
            _dashboard = dashboard;
            _mapper = mapper;
        }

        #region Export Documents
        public async Task<ExportFileDto> ExportDocumentsAsync(DocumentSearchCriteria c, string format)
        {
            var userId = _userCtx.UserId ?? "";
            var actorScope = await _actorScopeProvider.GetScopeAsync(userId);

            var docs = await _documentRepository.ListAsync(actorScope, c);
            var rows = _mapper.Map<List<DocumentReportRowDto>>(docs);

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
                IncludeLogo = true,
                IncludeFooter = true,
                ColumnMappings = new Dictionary<string, string>
                {
                    ["DocumentNumber"] = "رقم الوثيقة",
                    ["Title"] = "العنوان",
                    ["CreatedAt"] = "تاريخ الإنشاء",
                    ["CreatedBy"] = "أنشئ بواسطة",
                }
            };

            return format.ToLower() == "pdf"
                ? await _export.ExportToPdfAsync(rows, opt)
                : await _export.ExportToExcelAsync(rows, opt);
        }
        #endregion

        #region Export Audit Logs
        public async Task<ExportFileDto> ExportAuditLogsAsync(AuditLogFilter filter, string format)
        {
            filter ??= new AuditLogFilter();

            var userId = _userCtx.UserId ?? "";
            var actorScope = await _actorScopeProvider.GetScopeAsync(userId);

            var q = _auditLogRepository.Query().AsQueryable();

            if (!actorScope.IsSuperAdmin && !actorScope.IsBranchManager)
                q = q.Where(a => a.UserId == actorScope.UserId);

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

            ApplyDateRangeForExport(ref q, filter.From, filter.To, out var periodText);

            q = q.OrderByDescending(a => a.Timestamp);

            var list = await q.ToListAsync();
            var rows = _mapper.Map<List<AuditLogReportRowDto>>(list);

            var opt = new ExportOptions
            {
                ReportName = "AuditLogs_Report",
                SheetName = "AuditLogs",
                CustomTitle = "تقرير سجل التدقيق",
                PeriodText = periodText,
                IncludeLogo = true,
                IncludeFooter = true,
                ColumnMappings = new Dictionary<string, string>
                {
                    ["Timestamp"] = "التاريخ",
                    ["UserName"] = "المستخدم",
                    ["ActionType"] = "الإجراء",
                    ["TableName"] = "اسم الجدول",
                    ["DocumentId"] = "رقم السطر",
                }
            };

            return format.ToLower() == "pdf"
                ? await _export.ExportToPdfAsync(rows, opt)
                : await _export.ExportToExcelAsync(rows, opt);
        }

        private static void ApplyDateRangeForExport(ref IQueryable<AuditLog> q, DateTimeOffset? from, DateTimeOffset? to, out string? periodText)
        {
            periodText = null;
            if (!from.HasValue && !to.HasValue) return;

            DateTimeOffset? start = null;
            DateTimeOffset? endExclusive = null;

            if (from.HasValue)
                start = from.Value;

            if (to.HasValue)
            {
                var t = to.Value;
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
        public async Task<ExportFileDto> ExportDashboardAsync(DashboardFilter filter, string format)
        {
            var dto = await _dashboard.GetAsync(filter);

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
                IncludeLogo = true,
                ColumnMappings = new Dictionary<string, string>
                {
                    ["Name"] = "البند",
                    ["Value"] = "القيمة"
                }
            };

            return format.ToLower() == "pdf"
                ? await _export.ExportToPdfAsync(rows, opt)
                : await _export.ExportToExcelAsync(rows, opt);
        }
        #endregion
    }
}
