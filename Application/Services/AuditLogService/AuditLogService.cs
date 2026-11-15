using Application.Common.Localization;
using Application.Common.Permissions;
using Application.Common.Results;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.ModelsDto.AuditLogDtos;
using Domain.Entities.Audit;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    /// <summary>
    /// استعراض سجل التدقيق للمدقق دون أي مسار تعديل/حذف (FR-077 / BR-054).
    /// </summary>
    public class AuditLogService : IAuditLogService
    {
        private readonly IAuditLogRepository _auditLogs;
        private readonly IUserRepository _users;
        private readonly IMedicalCenterRepository _centers;
        private readonly IPermissionGate _permissionGate;
        private readonly IMapper _mapper;

        public AuditLogService(
            IAuditLogRepository auditLogs,
            IUserRepository users,
            IMapper mapper,
            IPermissionGate permissionGate,
            IMedicalCenterRepository centers)
        {
            _auditLogs = auditLogs;
            _users = users;
            _mapper = mapper;
            _permissionGate = permissionGate;
            _centers = centers;
        }

        public async Task<AuditLogDetailsDto> GetByIdAsync(long id)
        {
            _permissionGate.EnsureAll(SystemPermissions.AuditLog.Read);

            var entity = await _auditLogs.Query()
                         .FirstOrDefaultAsync(a => a.EventId == id)
                ?? throw new ReportNotFoundException("سجل التدقيق غير موجود.");

            var dto = _mapper.Map<AuditLogDetailsDto>(entity);
            await EnrichAsync([dto], [entity]);
            return dto;
        }

        public async Task<PaginatedList<AuditLogListDto>> GetAllAsync(int pageNumber, int pageSize)
        {
            return await GetAllFilteredAsync(pageNumber, pageSize, new AuditLogFilter());
        }

        public async Task<PaginatedList<AuditLogListDto>> GetAllFilteredAsync(
            int pageNumber,
            int pageSize,
            AuditLogFilter auditLogFilter)
        {
            _permissionGate.EnsureAll(SystemPermissions.AuditLog.Read);

            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 500) pageSize = 500;

            var scoped = await AuditEventQuery.ApplyAsync(
                _auditLogs.Query(),
                auditLogFilter ?? new AuditLogFilter(),
                _users);

            scoped = scoped.OrderByDescending(a => a.OccurredAt);

            var total = await scoped.CountAsync();
            var list = await scoped
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var mapped = _mapper.Map<List<AuditLogListDto>>(list);
            await EnrichAsync(mapped, list);
            return new PaginatedList<AuditLogListDto>(mapped, total, pageNumber, pageSize);
        }

        public async Task<AuditLogSummaryDto> GetSummaryAsync()
        {
            _permissionGate.EnsureAll(SystemPermissions.AuditLog.Read);

            var scoped = _auditLogs.Query();
            var grouped = await scoped
                .GroupBy(a => a.EventType)
                .Select(g => new { ActionType = g.Key, Count = g.LongCount() })
                .ToListAsync();

            long added = grouped.FirstOrDefault(x => x.ActionType == "Added")?.Count ?? 0;
            long modified = grouped.FirstOrDefault(x => x.ActionType == "Modified")?.Count ?? 0;
            long deleted = grouped.FirstOrDefault(x => x.ActionType == "Deleted")?.Count ?? 0;
            long total = await scoped.LongCountAsync();

            return new AuditLogSummaryDto
            {
                Total = total,
                Added = added,
                Modified = modified,
                Deleted = deleted
            };
        }

        public async Task<List<string>> GetEntityTypeNamesAsync()
        {
            _permissionGate.EnsureAll(SystemPermissions.AuditLog.Read);
            return await _auditLogs.GetEntityTypeNamesAsync();
        }

        private async Task EnrichAsync(IReadOnlyList<AuditLogListDto> items, IReadOnlyList<AuditEvent> events)
        {
            var names = await _users.GetDisplayNamesByIdsAsync(
                items.Select(item => item.UserId).Where(id => !string.IsNullOrWhiteSpace(id)));
            var centerNames = await AuditPresentation.LoadCurrentCenterNamesAsync(
                _centers,
                events.Select(auditEvent => auditEvent.CenterId));

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (names.TryGetValue(item.UserId, out var name))
                    item.UserName = name;

                var display = AuditPresentation.Present(events[i]);
                item.EntityDisplayName = display.EntityDisplayName;
                item.ChangedValuesSummary = display.ChangedValuesSummary;
                if (item.CenterId is Guid centerId && centerNames.TryGetValue(centerId, out var centerName))
                    item.CenterName = centerName;

                if (item is AuditLogDetailsDto details)
                    details.ChangedFields = display.ChangedFields;
            }
        }
    }
}
