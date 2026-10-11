using Domain.Entities.Auth;

namespace Application.Interfaces.Repositories;

public interface IRefreshTokenRepository
{
    void Add(UserRefreshToken token);
    Task<UserRefreshToken?> GetValidByTokenAsync(string refreshToken, DateTime now, CancellationToken cancellationToken = default);
    Task<UserRefreshToken?> GetByTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    /// <summary>
    /// يبطل التوكن الصالح ذرّياً. يعيد الصف فقط إذا نجح التحديث الشرطي (صف واحد).
    /// </summary>
    Task<UserRefreshToken?> TryConsumeValidByTokenHashAsync(string tokenHash, DateTime now, CancellationToken cancellationToken = default);
    Task MarkRotatedAsync(Guid consumedId, Guid replacementId, CancellationToken cancellationToken = default);
    Task SetRevokeReasonAsync(Guid tokenId, string reason, CancellationToken cancellationToken = default);
    Task RevokeActiveFamilyAsync(Guid tokenFamilyId, string reason, CancellationToken cancellationToken = default);
    Task<bool> HasActiveInFamilyAsync(Guid tokenFamilyId, DateTime now, CancellationToken cancellationToken = default);
    void Remove(UserRefreshToken token);
    void MarkRevoked(UserRefreshToken token);
    Task<List<UserRefreshToken>> GetActiveByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task RevokeAllAsync(CancellationToken cancellationToken = default);
}
