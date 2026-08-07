using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services
{
    public interface IAuthService
    {
        Task<(TokenGenerationResultDto Tokens, string UserId)> LoginAsync(RequestToken requestToken);
        Task<MeResponseDto> GetMeAsync(string? userId);
        Task<string> ValidateAndGetUserIdAsync(string? userId);
        Task<(TokenResponseDto Tokens, string UserId)> RefreshSessionAsync(string? refreshToken);
        Task LogoutAsync(string? refreshToken);
    }
}
