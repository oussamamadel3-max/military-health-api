using Application.Interfaces.Repositories;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly AppDbContext _context;

    public AuditLogRepository(AppDbContext context) => _context = context;

    public IQueryable<AuditLog> Query(bool asNoTracking = true)
        => asNoTracking ? _context.AuditLogs.AsNoTracking() : _context.AuditLogs;

    public async Task<AuditLog?> GetByIdAsync(long logId, IQueryable<AuditLog> scopedQuery, CancellationToken cancellationToken = default)
        => await scopedQuery.FirstOrDefaultAsync(a => a.LogId == logId, cancellationToken);

    public async Task<List<string>> GetEntityTypeNamesAsync(CancellationToken cancellationToken = default)
    {
        return _context.Model.GetEntityTypes()
            .Select(t => t.ClrType.Name)
            .Where(n => n != nameof(AuditLog))
            .Distinct()
            .OrderBy(n => n)
            .ToList();
    }
}
