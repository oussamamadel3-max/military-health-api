using ArchiveProject.Application.Dtos.AuthDtos;

namespace ArchiveProject.Application.Common.Interfaces.AuthInterfaces
{
    public interface ITokenService
    {
        Task<TokenGenerationResultDto> GenerateTokensAsync(string userId);
        Task<TokenResponseDto> RefreshAccessTokenAsync(string refreshToken);
        Task<bool> RevokeRefreshTokenAsync(string refreshToken);
    }

}

