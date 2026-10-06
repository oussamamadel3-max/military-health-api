using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services
{
    public interface ITokenService
    {
        Task<TokenGenerationResultDto> GenerateTokensAsync(string userId, Guid? activeCenterId = null);
        Task<TokenResponseDto> RefreshAccessTokenAsync(string refreshToken, Guid? activeCenterId = null);
        Task<bool> RevokeRefreshTokenAsync(string refreshToken);
    }

}

