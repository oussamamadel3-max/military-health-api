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

    public void Remove(UserRefreshToken token) => _context.UserRefreshTokens.Remove(token);

    public void MarkRevoked(UserRefreshToken token) => token.IsRevoked = true;

    public async Task<List<UserRefreshToken>> GetActiveByUserIdAsync(string userId, CancellationToken cancellationToken = default)
        => await _context.UserRefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);
}
