using Application.Interfaces.Repositories;
using Domain.Entities.Audit;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly AppDbContext _context;

    public AuditLogRepository(AppDbContext context) => _context = context;

    public IQueryable<AuditEvent> Query(bool asNoTracking = true)
        => asNoTracking ? _context.AuditEvents.AsNoTracking() : _context.AuditEvents;

    public void Add(AuditEvent auditEvent) => _context.AuditEvents.Add(auditEvent);

    public async Task<AuditEvent?> GetByIdAsync(long eventId, IQueryable<AuditEvent> scopedQuery, CancellationToken cancellationToken = default)
        => await scopedQuery.FirstOrDefaultAsync(a => a.EventId == eventId, cancellationToken);

    public Task<List<string>> GetEntityTypeNamesAsync(CancellationToken cancellationToken = default)
    {
        var names = _context.Model.GetEntityTypes()
            .Select(t => t.ClrType.Name)
            .Where(n => n != nameof(AuditEvent) && n != nameof(CrossCenterAccess))
            .Distinct()
            .OrderBy(n => n)
            .ToList();
        return Task.FromResult(names);
    }
}
