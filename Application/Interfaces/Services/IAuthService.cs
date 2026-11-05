using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services
{
    public interface IAuthService
    {
        Task<LoginResult> LoginAsync(RequestToken requestToken);
        Task<MeResponseDto> GetMeAsync(string? userId);
        Task<string> ValidateAndGetUserIdAsync(string? userId);
        Task<(TokenResponseDto Tokens, string UserId)> RefreshSessionAsync(string? refreshToken, Guid? activeCenterId = null);
        /// <summary>
        /// MH-SEC-052: ends the current session family using the validated <paramref name="sessionFamilyId"/> claim
        /// (refresh cookie optional). Idempotent when the family is already inactive.
        /// </summary>
        Task LogoutAsync(string? refreshToken, Guid? sessionFamilyId = null, Guid? activeCenterId = null);
    }
}
