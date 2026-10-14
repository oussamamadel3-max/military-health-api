using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class CenterClinicRepository : ICenterClinicRepository
{
    private readonly AppDbContext _context;

    public CenterClinicRepository(AppDbContext context) => _context = context;

    public IQueryable<CenterClinic> Query(bool asNoTracking = true)
    {
        var query = _context.CenterClinics
            .Include(x => x.Clinic)
            .Include(x => x.Center)
            .AsQueryable();
        return asNoTracking ? query.AsNoTracking() : query;
    }

    public async Task<CenterClinic?> GetByIdAsync(
        Guid centerClinicId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.CenterClinics
            .Include(x => x.Clinic)
            .Include(x => x.Center)
            .AsQueryable();
        if (asNoTracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(x => x.CenterClinicId == centerClinicId, cancellationToken);
    }

    public async Task<CenterClinic?> GetByCenterAndClinicAsync(
        Guid centerId,
        Guid clinicId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.CenterClinics
            .Include(x => x.Clinic)
            .Include(x => x.Center)
            .AsQueryable();
        if (asNoTracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(
            x => x.CenterId == centerId && x.ClinicId == clinicId,
            cancellationToken);
    }

    public void Add(CenterClinic link) => _context.CenterClinics.Add(link);
}
