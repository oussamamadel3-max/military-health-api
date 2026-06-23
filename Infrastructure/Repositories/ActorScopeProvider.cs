using Application.Interfaces;
using Domain.Entities.Auth;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ActorScopeProvider : IActorScopeProvider
{
    private readonly AppDbContext _context;

    public ActorScopeProvider(AppDbContext context) => _context = context;

    public async Task<ActorScope> GetScopeAsync(string userId, CancellationToken cancellationToken = default)
    {
        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        var normalizedRoleNames = await GetNormalizedRoleNamesAsync(userId, cancellationToken);
        var isSuperAdmin = normalizedRoleNames.Any(n => n == "SUPERADMIN");
        var isBranchManager = normalizedRoleNames.Any(n => n == "BRANCHMANAGER");
        return new ActorScope(actor, userId, isSuperAdmin, isBranchManager, normalizedRoleNames);
    }

    public async Task<IReadOnlyList<string>> GetNormalizedRoleNamesAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserRoleAssociations
            .AsNoTracking()
            .Where(ura => ura.UserId == userId)
            .Join(_context.Roles.AsNoTracking(), ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
            .Where(n => n != null)
            .Select(n => n!)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsSuperAdminAsync(string userId, CancellationToken cancellationToken = default)
    {
        var names = await GetNormalizedRoleNamesAsync(userId, cancellationToken);
        return names.Any(n => n == "SUPERADMIN");
    }

    public async Task<bool> IsBranchManagerAsync(string userId, CancellationToken cancellationToken = default)
    {
        var names = await GetNormalizedRoleNamesAsync(userId, cancellationToken);
        return names.Any(n => n == "BRANCHMANAGER");
    }
}
