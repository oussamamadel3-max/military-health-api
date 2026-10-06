using Application.Common.Results;
using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Infrastructure.Repositories;

public class OrgUnitTypeRepository : RepositoryBase<OrgUnitType, Guid>, IOrgUnitTypeRepository
{
    public OrgUnitTypeRepository(AppDbContext context) : base(context) { }
    protected override DbSet<OrgUnitType> DbSet => Context.OrgUnitTypes;
    protected override Expression<Func<OrgUnitType, bool>> IdPredicate(Guid id) => x => x.OrgUnitTypeId == id;

    public async Task<PaginatedList<OrgUnitType>> GetPagedAsync(int page, int size, string? query, bool? isActive, CancellationToken ct = default)
    {
        var q = DbSet.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(x => x.Code.Contains(term) || x.NameAr.Contains(term));
        }
        if (isActive.HasValue) q = q.Where(x => x.IsActive == isActive);
        q = q.OrderBy(x => x.DisplayOrder).ThenBy(x => x.NameAr);
        var count = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(items, count, page, size);
    }

    public async Task<IReadOnlyList<OrgUnitType>> GetActiveAsync(CancellationToken ct = default) =>
        await DbSet.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.NameAr).ToListAsync(ct);

    public Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default) =>
        DbSet.AnyAsync(x => x.Code == code && (!excludeId.HasValue || x.OrgUnitTypeId != excludeId), ct);
}
