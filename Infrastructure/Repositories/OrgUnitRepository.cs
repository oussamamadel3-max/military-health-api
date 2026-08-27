using Application.Common;
using Application.Common.Results;
using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Infrastructure.Repositories;

public class OrgUnitRepository : RepositoryBase<OrgUnit, Guid>, IOrgUnitRepository
{
    public OrgUnitRepository(AppDbContext context) : base(context) { }
    protected override DbSet<OrgUnit> DbSet => Context.OrgUnits;
    protected override Expression<Func<OrgUnit, bool>> IdPredicate(Guid id) => x => x.OrgUnitId == id;

    private IQueryable<OrgUnit> Detailed(bool tracked = false)
    {
        var query = DbSet.Include(x => x.OrgUnitType).Include(x => x.ParentOrgUnit);
        return tracked ? query : query.AsNoTracking();
    }

    public Task<OrgUnit?> GetDetailedAsync(Guid id, CancellationToken ct = default) =>
        Detailed().FirstOrDefaultAsync(x => x.OrgUnitId == id, ct);

    public async Task<PaginatedList<OrgUnit>> GetPagedAsync(int page, int size, string? query, string? status, CancellationToken ct = default)
    {
        var q = Detailed();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(x => x.NameAr.Contains(term) || (x.Code != null && x.Code.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToUpperInvariant();
            q = q.Where(x => x.Status == normalized);
        }
        q = q.OrderBy(x => x.NameAr);
        var count = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(items, count, page, size);
    }

    public async Task<IReadOnlyList<OrgUnit>> GetDropdownAsync(CancellationToken ct = default) =>
        await DbSet.AsNoTracking().Where(x => x.Status == CoreStatuses.Active).OrderBy(x => x.NameAr).ToListAsync(ct);

    public async Task<IReadOnlyList<OrgUnit>> GetTreeUnitsAsync(string? status, CancellationToken ct = default)
    {
        var q = Detailed();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToUpperInvariant();
            q = q.Where(x => x.Status == normalized);
        }
        return await q.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OrgUnit>> GetChildrenAsync(Guid parentId, CancellationToken ct = default) =>
        await Detailed().Where(x => x.ParentOrgUnitId == parentId).OrderBy(x => x.NameAr).ToListAsync(ct);

    public async Task<IReadOnlyList<OrgUnit>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        return await Detailed().Where(x => list.Contains(x.OrgUnitId)).OrderBy(x => x.NameAr).ToListAsync(ct);
    }

    public Task<bool> ExistsAsync(Guid id, bool excludeArchived = false, CancellationToken ct = default) =>
        DbSet.AnyAsync(x => x.OrgUnitId == id && (!excludeArchived || x.Status != CoreStatuses.Archived), ct);
    public Task<bool> AnyAsync(CancellationToken ct = default) => DbSet.AnyAsync(ct);
    public Task<bool> HasActiveChildrenAsync(Guid id, CancellationToken ct = default) =>
        DbSet.AnyAsync(x => x.ParentOrgUnitId == id && x.Status == CoreStatuses.Active, ct);
    public Task<bool> CodeExistsAsync(string code, Guid? excludeId, CancellationToken ct = default) =>
        DbSet.AnyAsync(x => x.Code == code && (!excludeId.HasValue || x.OrgUnitId != excludeId), ct);
    public Task<bool> SiblingNameExistsAsync(Guid? parentId, string name, Guid? excludeId, CancellationToken ct = default) =>
        DbSet.AnyAsync(x => x.ParentOrgUnitId == parentId && x.NameAr == name && x.Status != CoreStatuses.Archived &&
                            (!excludeId.HasValue || x.OrgUnitId != excludeId), ct);

    public async Task<IReadOnlyList<(Guid Id, Guid? ParentId)>> GetNonArchivedParentLinksAsync(CancellationToken ct = default) =>
        await DbSet.AsNoTracking().Where(x => x.Status != CoreStatuses.Archived)
            .Select(x => new ValueTuple<Guid, Guid?>(x.OrgUnitId, x.ParentOrgUnitId)).ToListAsync(ct);
}
