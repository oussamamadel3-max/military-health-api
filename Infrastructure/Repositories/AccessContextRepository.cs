using Application.Common.Security;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class AccessContextRepository : IAccessContextRepository
{
    private readonly AppDbContext _context;

    public AccessContextRepository(AppDbContext context) => _context = context;

    public async Task<AccessSnapshot> LoadAsync(string userId, CancellationToken cancellationToken = default)
    {
        var roles = await _context.UserRoleAssociations.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => new CenterPermissionEvaluator.RoleAssignment(
                a.RoleId,
                a.Role.Name,
                a.CenterId,
                a.IsActive,
                a.Role.IsActive))
            .ToListAsync(cancellationToken);

        var directs = await _context.UserPermissions.AsNoTracking()
            .Where(a => a.UserId == userId && a.Permission != null)
            .Select(a => new CenterPermissionEvaluator.DirectAssignment(
                a.Permission!.Name,
                a.CenterId,
                a.IsActive,
                a.Permission.IsActive))
            .ToListAsync(cancellationToken);

        var roleIds = roles.Select(r => r.RoleId).Distinct().ToList();
        var links = roleIds.Count == 0
            ? new List<CenterPermissionEvaluator.RolePermissionLink>()
            : await _context.RolePermissions.AsNoTracking()
                .Where(rp => roleIds.Contains(rp.RoleId) && rp.Permission != null)
                .Select(rp => new CenterPermissionEvaluator.RolePermissionLink(
                    rp.RoleId,
                    rp.Permission.Name,
                    rp.IsActive,
                    rp.Permission.IsActive))
                .ToListAsync(cancellationToken);

        return new AccessSnapshot { Roles = roles, Directs = directs, Links = links };
    }

    public async Task<IReadOnlyList<AssignedCenterDto>> GetActiveCentersAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserCenters.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive && x.Center.IsActive)
            .OrderBy(x => x.Center.NameAr)
            .Select(x => new AssignedCenterDto
            {
                CenterId = x.CenterId,
                Code = x.Center.Code,
                NameAr = x.Center.NameAr
            })
            .ToListAsync(cancellationToken);
    }

    public Task<bool> IsActivelyAssignedAsync(string userId, Guid centerId, CancellationToken cancellationToken = default)
        => _context.UserCenters.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.CenterId == centerId && x.IsActive && x.Center.IsActive, cancellationToken);
}
