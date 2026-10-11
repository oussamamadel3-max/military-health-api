using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services
{
    public interface IAuthService
    {
        Task<LoginResult> LoginAsync(RequestToken requestToken);
        Task<MeResponseDto> GetMeAsync(string? userId);
        Task<string> ValidateAndGetUserIdAsync(string? userId);
        Task<(TokenResponseDto Tokens, string UserId)> RefreshSessionAsync(string? refreshToken, Guid? activeCenterId = null);
        Task LogoutAsync(string? refreshToken);
    }
}
