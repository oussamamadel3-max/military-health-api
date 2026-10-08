using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services
{
    public interface ITokenService
    {
        Task<TokenGenerationResultDto> GenerateTokensAsync(string userId);
        Task<TokenResponseDto> RefreshAccessTokenAsync(string refreshToken);
        Task<bool> RevokeRefreshTokenAsync(string refreshToken);
    }

}

