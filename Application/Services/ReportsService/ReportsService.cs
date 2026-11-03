using System.Net;
using System.Text.Json;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
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
        public const string AuditExportEventType = "AuditLogExported";

        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IUserRepository _users;
        private readonly IExportService _export;
        private readonly IDashboardService _dashboard;
        private readonly IMapper _mapper;
        private readonly IPermissionGate _permissionGate;
        private readonly ICurrentUserService _currentUser;
        private readonly IUnitOfWork _unitOfWork;

        public ReportsService(
            IAuditLogRepository auditLogRepository,
            IUserRepository users,
            IExportService export,
            IDashboardService dashboard,
            IMapper mapper,
            IPermissionGate permissionGate,
            ICurrentUserService currentUser,
            IUnitOfWork unitOfWork)
        {
            _auditLogRepository = auditLogRepository;
            _users = users;
            _export = export;
            _dashboard = dashboard;
            _mapper = mapper;
            _permissionGate = permissionGate;
            _currentUser = currentUser;
            _unitOfWork = unitOfWork;
        }

        #region Export Audit Logs
        public async Task<ExportFileDto> ExportAuditLogsAsync(AuditLogFilter filter, string format)
        {
            _permissionGate.EnsureAll(SystemPermissions.AuditLog.Export);

            filter ??= new AuditLogFilter();
            var q = await AuditEventQuery.ApplyAsync(_auditLogRepository.Query(), filter, _users);
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

            var periodText = BuildPeriodText(filter.From, filter.To);
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
                NonSummarizedColumns = ["LogId"],
                ColumnMappings = new Dictionary<string, string>
                {
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

            var file = format.ToLower() == "pdf"
                ? await _export.ExportToPdfAsync(rows, opt)
                : await _export.ExportToExcelAsync(rows, opt);

            // FR-077: تسجيل عملية الاستخراج نفسها دون تضمين محتوى التقرير.
            _auditLogRepository.Add(new AuditEvent
            {
                OccurredAt = DateTimeOffset.UtcNow,
                ActorUserId = string.IsNullOrWhiteSpace(_currentUser.UserId) || _currentUser.UserId == "0"
                    ? null
                    : _currentUser.UserId,
                ContextScope = CenterClaimTypes.Central,
                CenterId = null,
                EventType = AuditExportEventType,
                Result = "SUCCESS",
                EntityType = nameof(AuditEvent),
                EntityId = null,
                ReasonText = "استخراج تقرير سجل التدقيق",
                NewValues = JsonSerializer.Serialize(new
                {
                    Format = format,
                    RecordCount = rows.Count,
                    filter.centerId,
                    filter.eventType,
                    filter.entityType,
                    filter.actorUserId,
                    filter.From,
                    filter.To
                }),
                UserAgent = _currentUser.UserAgent,
                CorrelationId = _currentUser.CorrelationId == Guid.Empty
                    ? Guid.NewGuid()
                    : _currentUser.CorrelationId,
                IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
            });
            await _unitOfWork.SaveChangesAsync();

            return file;
        }

        private static string? BuildPeriodText(DateTimeOffset? from, DateTimeOffset? to)
        {
            if (!from.HasValue && !to.HasValue) return null;
            var fromText = from.HasValue ? from.Value.ToString("yyyy/MM/dd") : "—";
            var toText = to.HasValue ? to.Value.ToString("yyyy/MM/dd") : "—";
            return $"{fromText} → {toText}";
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
