using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services
{
    public interface IAuthService
    {
        Task<TokenGenerationResultDto> LoginAsync(RequestToken requestToken);
        Task<MeResponseDto> GetMeAsync(string userId);
    }
}
