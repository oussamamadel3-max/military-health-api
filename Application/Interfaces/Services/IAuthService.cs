using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services
{
    public interface IAuthService
    {
        //Task<Result<LoginResponseDto>> LoginAsync(RequestToken requestToken);
        Task<Result<TokenGenerationResultDto>> LoginAsync(RequestToken requestToken);
        Task<Result<MeResponseDto>> GetMeAsync(string userId);
    }
}

