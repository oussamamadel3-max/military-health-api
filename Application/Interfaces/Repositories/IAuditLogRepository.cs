using Domain.Entities.Audit;

namespace Application.Interfaces.Repositories;

public interface IAuditLogRepository
{
    IQueryable<AuditEvent> Query(bool asNoTracking = true);
    Task<AuditEvent?> GetByIdAsync(long eventId, IQueryable<AuditEvent> scopedQuery, CancellationToken cancellationToken = default);
    Task<List<string>> GetEntityTypeNamesAsync(CancellationToken cancellationToken = default);
}
