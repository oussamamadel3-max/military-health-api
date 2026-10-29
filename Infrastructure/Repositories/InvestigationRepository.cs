using Application.Interfaces.Repositories;
using Domain.Entities.Medical;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>استعلامات وكتابة سجلات الاستقصاءات عبر EF.</summary>
public class InvestigationRepository : IInvestigationRepository
{
    private readonly AppDbContext _context;

    public InvestigationRepository(AppDbContext context) => _context = context;

    /// <inheritdoc />
    public IQueryable<Investigation> Query(bool asNoTracking = true)
        => asNoTracking ? _context.Investigations.AsNoTracking() : _context.Investigations;

    /// <inheritdoc />
    public Task<Investigation?> GetByIdInCenterAsync(
        Guid investigationId,
        Guid centerId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = asNoTracking ? _context.Investigations.AsNoTracking() : _context.Investigations;
        return query.FirstOrDefaultAsync(
            i => i.InvestigationId == investigationId && i.CenterId == centerId,
            cancellationToken);
    }

    /// <inheritdoc />
    public void Add(Investigation investigation) => _context.Investigations.Add(investigation);
}