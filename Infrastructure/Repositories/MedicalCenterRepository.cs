using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class MedicalCenterRepository : IMedicalCenterRepository
{
    private readonly AppDbContext _context;

    public MedicalCenterRepository(AppDbContext context) => _context = context;

    public IQueryable<MedicalCenter> Query(bool asNoTracking = true)
    {
        var query = _context.MedicalCenters.AsQueryable();
        return asNoTracking ? query.AsNoTracking() : query;
    }

    public async Task<MedicalCenter?> GetByIdAsync(Guid centerId, bool asNoTracking = false, CancellationToken cancellationToken = default)
    {
        var query = _context.MedicalCenters.AsQueryable();
        if (asNoTracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(c => c.CenterId == centerId, cancellationToken);
    }

    public Task<bool> CodeExistsAsync(string code, Guid? excludingCenterId = null, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim();
        var query = _context.MedicalCenters.AsNoTracking()
            .Where(c => c.Code.ToLower() == normalized.ToLower());
        if (excludingCenterId.HasValue)
            query = query.Where(c => c.CenterId != excludingCenterId.Value);
        return query.AnyAsync(cancellationToken);
    }

    public void Add(MedicalCenter center) => _context.MedicalCenters.Add(center);

    /// <inheritdoc />
    public async Task LockAsync(Guid centerId, CancellationToken cancellationToken = default)
    {
        var provider = _context.Database.ProviderName ?? string.Empty;
        if (!provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            return;

        await _context.Database.ExecuteSqlRawAsync(
            "SELECT \"centerId\" FROM core.\"medicalCenter\" WHERE \"centerId\" = {0} FOR UPDATE",
            new object[] { centerId },
            cancellationToken);
    }

    public Task<int> CountActiveUsersAsync(Guid centerId, CancellationToken cancellationToken = default)
        => _context.UserCenters.AsNoTracking()
            .CountAsync(uc => uc.CenterId == centerId && uc.IsActive, cancellationToken);

    public Task<int> CountActiveClinicsAsync(Guid centerId, CancellationToken cancellationToken = default)
        => _context.CenterClinics.AsNoTracking()
            .CountAsync(cc => cc.CenterId == centerId && cc.IsActive, cancellationToken);
}
