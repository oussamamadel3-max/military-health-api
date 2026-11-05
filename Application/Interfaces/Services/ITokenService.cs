using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services
{
    public interface ITokenService
    {
        Task<TokenGenerationResultDto> GenerateTokensAsync(string userId, Guid? activeCenterId = null);
        Task<TokenResponseDto> RefreshAccessTokenAsync(string refreshToken, Guid? activeCenterId = null);
        Task<bool> RevokeRefreshTokenAsync(string refreshToken);

        /// <summary>
        /// MH-SEC-052: ends the current refresh-token family for Logout.
        /// Returns true when the family has no active refresh tokens afterward (including already-ended families).
        /// </summary>
        Task<bool> EndSessionFamilyAsync(
            Guid? sessionFamilyId,
            string? rawRefreshToken,
            CancellationToken cancellationToken = default);
    }

}

