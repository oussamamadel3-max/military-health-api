using Domain.Entities.Auth;

namespace Application.Interfaces.Repositories;

public interface IRefreshTokenRepository
{
    void Add(UserRefreshToken token);
    Task<UserRefreshToken?> GetValidByTokenAsync(string refreshToken, DateTime now, CancellationToken cancellationToken = default);
    Task<UserRefreshToken?> GetByTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    void Remove(UserRefreshToken token);
    void MarkRevoked(UserRefreshToken token);
    Task<List<UserRefreshToken>> GetActiveByUserIdAsync(string userId, CancellationToken cancellationToken = default);
}
