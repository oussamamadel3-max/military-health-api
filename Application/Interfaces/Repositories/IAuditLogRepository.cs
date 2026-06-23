using Domain.Entities;

namespace Application.Interfaces.Repositories;

public interface IAuditLogRepository
{
    IQueryable<AuditLog> Query(bool asNoTracking = true);
    Task<AuditLog?> GetByIdAsync(long logId, IQueryable<AuditLog> scopedQuery, CancellationToken cancellationToken = default);
    Task<List<string>> GetEntityTypeNamesAsync(CancellationToken cancellationToken = default);
}
