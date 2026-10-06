using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Common.Utilities;
using Application.Dtos.DashboardDtos;
using Application.Dtos.ExportDtos;
using Application.Dtos.ReportDtos;
using Application.ModelsDto.AuditLogDtos;
using Domain.Entities.Audit;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Reports
{
    public class ReportsService : IReportsService
    {
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IUserRepository _users;
        private readonly IUserContextService _userCtx;
        private readonly IExportService _export;
        private readonly IDashboardService _dashboard;
        private readonly IMapper _mapper;

        public ReportsService(
            IAuditLogRepository auditLogRepository,
            IUserRepository users,
            IUserContextService userCtx,
            IExportService export,
            IDashboardService dashboard,
            IMapper mapper)
        {
            _auditLogRepository = auditLogRepository;
            _users = users;
            _userCtx = userCtx;
            _export = export;
            _dashboard = dashboard;
            _mapper = mapper;
        }

        #region Export Audit Logs
        public async Task<ExportFileDto> ExportAuditLogsAsync(AuditLogFilter filter, string format)
        {
            filter ??= new AuditLogFilter();

            var q = _auditLogRepository.Query().AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.searchUserName))
                q = await FilterByActorNameAsync(q, filter.searchUserName);

            if (!string.IsNullOrWhiteSpace(filter.userName))
                q = await FilterByActorNameAsync(q, filter.userName);

            if (filter.actionType.HasValue)
            {
                var at = filter.actionType.Value.ToString();
                q = q.Where(a => a.EventType == at);
            }

            if (filter.tableNames != null && filter.tableNames.Count > 0)
            {
                var set = filter.tableNames
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim().ToLower())
                    .Distinct()
                    .ToList();

                if (set.Count > 0)
                    q = q.Where(a => set.Contains((a.EntityType ?? "").ToLower()));
            }

            ApplyDateRangeForExport(ref q, filter.From, filter.To, out var periodText);

            q = q.OrderByDescending(a => a.OccurredAt);

            var list = await q.ToListAsync();
            var rows = _mapper.Map<List<AuditLogReportRowDto>>(list);
            var names = await _users.GetDisplayNamesByIdsAsync(list.Select(x => x.ActorUserId).Where(id => !string.IsNullOrWhiteSpace(id))!);
            for (var i = 0; i < rows.Count; i++)
            {
                var actorId = list[i].ActorUserId;
                if (!string.IsNullOrWhiteSpace(actorId) && names.TryGetValue(actorId, out var name))
                    rows[i].UserName = name;
            }

            var opt = new ExportOptions
            {
                ReportName = "AuditLogs_Report",
                SheetName = "AuditLogs",
                CustomTitle = "تقرير سجل التدقيق",
                PeriodText = periodText,
                IncludeLogo = true,
                IncludeFooter = true,
                IncludeSummary = true,
                AutoFilter = true,
                FreezeHeaders = true,
                // لا نجمع معرفات نصية/طويلة — الملخص يبقى عدد السجلات فقط
                NonSummarizedColumns = ["LogId"],
                ColumnMappings = new Dictionary<string, string>
                {
                    // ترتيب العرض من اليمين لليسار مع RTL: رقم السجل ← … ← التاريخ
                    ["LogId"] = "رقم السجل",
                    ["TableName"] = "اسم الجدول",
                    ["ActionType"] = "الإجراء",
                    ["UserName"] = "المستخدم",
                    ["EntityId"] = "معرف الكيان",
                    ["CenterId"] = "المركز",
                    ["Result"] = "النتيجة",
                    ["Timestamp"] = "التاريخ",
                }
            };

            return format.ToLower() == "pdf"
                ? await _export.ExportToPdfAsync(rows, opt)
                : await _export.ExportToExcelAsync(rows, opt);
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

        private static void ApplyDateRangeForExport(ref IQueryable<AuditEvent> q, DateTimeOffset? from, DateTimeOffset? to, out string? periodText)
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

            if (start.HasValue) q = q.Where(a => a.OccurredAt >= start.Value);
            if (endExclusive.HasValue) q = q.Where(a => a.OccurredAt < endExclusive.Value);

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
