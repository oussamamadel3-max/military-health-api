using Application.Common;
using Application.Common.Results;
using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Infrastructure.Repositories;

public class UserOrgScopeRepository : RepositoryBase<UserOrgScope, Guid>, IUserOrgScopeRepository
{
    public UserOrgScopeRepository(AppDbContext context) : base(context) { }
    protected override DbSet<UserOrgScope> DbSet => Context.UserOrgScopes;
    protected override Expression<Func<UserOrgScope, bool>> IdPredicate(Guid id) => x => x.UserOrgScopeId == id;

    private IQueryable<UserOrgScope> Detailed(bool tracked = false)
    {
        var query = DbSet.Include(x => x.User).Include(x => x.Permission).Include(x => x.OrgUnit);
        return tracked ? query : query.AsNoTracking();
    }

    public Task<UserOrgScope?> GetDetailedAsync(Guid id, CancellationToken ct = default) =>
        Detailed().FirstOrDefaultAsync(x => x.UserOrgScopeId == id, ct);

    public async Task<PaginatedList<UserOrgScope>> GetPagedAsync(int page, int size, string? query, string? status, CancellationToken ct = default)
    {
        var q = Detailed();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(x => (x.User != null && ((x.User.UserName != null && x.User.UserName.Contains(term)) ||
                                                 (x.User.FullName != null && x.User.FullName.Contains(term)))) ||
                             (x.Permission.Name != null && x.Permission.Name.Contains(term)) || x.OrgUnit.NameAr.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToUpperInvariant();
            q = q.Where(x => x.Status == normalized);
        }
        q = q.OrderByDescending(x => x.AssignedAt);
        var count = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(items, count, page, size);
    }

    public async Task<IReadOnlyList<UserOrgScope>> GetByUserIdAsync(string userId, bool activeOnly = false, CancellationToken ct = default)
    {
        var q = Detailed().Where(x => x.UserId == userId);
        if (activeOnly) q = q.Where(x => x.Status == CoreStatuses.Active);
        return await q.OrderByDescending(x => x.AssignedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OrgScopeLink>> GetActiveScopesForPermissionAsync(string userId, Guid permissionId, CancellationToken ct = default) =>
        await DbSet.AsNoTracking()
            .Where(x => x.UserId == userId && x.PermissionId == permissionId && x.Status == CoreStatuses.Active)
            .Select(x => new OrgScopeLink(x.OrgUnitId, x.ScopeMode)).ToListAsync(ct);

    public Task<bool> DuplicateExistsAsync(string userId, Guid permissionId, Guid orgUnitId, string scopeMode, Guid? excludeId, CancellationToken ct = default) =>
        DbSet.AnyAsync(x => x.UserId == userId && x.PermissionId == permissionId && x.OrgUnitId == orgUnitId &&
                            x.ScopeMode == scopeMode && (!excludeId.HasValue || x.UserOrgScopeId != excludeId), ct);
}
