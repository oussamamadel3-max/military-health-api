using Application.Common.Security;
using Application.Interfaces.Repositories;
using Domain.Entities.Auth;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _context;

    public RefreshTokenRepository(AppDbContext context) => _context = context;

    public void Add(UserRefreshToken token) => _context.UserRefreshTokens.Add(token);

    public async Task<UserRefreshToken?> GetValidByTokenAsync(string refreshToken, DateTime now, CancellationToken cancellationToken = default)
        => await _context.UserRefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken && !rt.IsRevoked && rt.ExpiryDate >= now, cancellationToken);

    public async Task<UserRefreshToken?> GetByTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
        => await _context.UserRefreshTokens.FirstOrDefaultAsync(rt => rt.Token == refreshToken, cancellationToken);

    public async Task<UserRefreshToken?> TryConsumeValidByTokenHashAsync(string tokenHash, DateTime now, CancellationToken cancellationToken = default)
    {
        var candidate = await _context.UserRefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(
                rt => rt.Token == tokenHash && !rt.IsRevoked && rt.ExpiryDate >= now,
                cancellationToken);

        if (candidate == null)
            return null;

        var affected = await _context.UserRefreshTokens
            .Where(rt => rt.Id == candidate.Id && !rt.IsRevoked && rt.ExpiryDate >= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rt => rt.IsRevoked, true)
                .SetProperty(rt => rt.RevokedAt, DateTimeOffset.UtcNow)
                .SetProperty(rt => rt.RevokeReason, "revoked"), cancellationToken);

        return affected == 1 ? candidate : null;
    }

    public async Task MarkRotatedAsync(Guid consumedId, Guid replacementId, CancellationToken cancellationToken = default)
    {
        await _context.UserRefreshTokens
            .Where(rt => rt.Id == consumedId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rt => rt.ReplacedByRefreshTokenId, replacementId)
                .SetProperty(rt => rt.RevokeReason, SessionRefresh.RotatedReason), cancellationToken);
    }

    public async Task SetRevokeReasonAsync(Guid tokenId, string reason, CancellationToken cancellationToken = default)
    {
        await _context.UserRefreshTokens
            .Where(rt => rt.Id == tokenId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rt => rt.RevokeReason, reason), cancellationToken);
    }

    public async Task RevokeActiveFamilyAsync(Guid tokenFamilyId, string reason, CancellationToken cancellationToken = default)
    {
        await _context.UserRefreshTokens
            .Where(rt => rt.TokenFamilyId == tokenFamilyId && !rt.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rt => rt.IsRevoked, true)
                .SetProperty(rt => rt.RevokedAt, DateTimeOffset.UtcNow)
                .SetProperty(rt => rt.RevokeReason, reason), cancellationToken);
    }

    public async Task<bool> HasActiveInFamilyAsync(Guid tokenFamilyId, DateTime now, CancellationToken cancellationToken = default)
        => await _context.UserRefreshTokens.AnyAsync(
            rt => rt.TokenFamilyId == tokenFamilyId && !rt.IsRevoked && rt.ExpiryDate >= now,
            cancellationToken);

    public void Remove(UserRefreshToken token) => _context.UserRefreshTokens.Remove(token);

    public void MarkRevoked(UserRefreshToken token)
    {
        token.IsRevoked = true;
        token.RevokedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(token.RevokeReason))
            token.RevokeReason = "revoked";
    }

    public async Task<List<UserRefreshToken>> GetActiveByUserIdAsync(string userId, CancellationToken cancellationToken = default)
        => await _context.UserRefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);

    public async Task RevokeAllAsync(CancellationToken cancellationToken = default)
    {
        await _context.UserRefreshTokens
            .Where(rt => !rt.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rt => rt.IsRevoked, true)
                .SetProperty(rt => rt.RevokedAt, DateTimeOffset.UtcNow)
                .SetProperty(rt => rt.RevokeReason, "revoked"), cancellationToken);
    }
}
