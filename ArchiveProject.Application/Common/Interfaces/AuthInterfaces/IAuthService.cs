using ArchiveProject.Application.Dtos.AuthDtos;

namespace ArchiveProject.Application.Common.Interfaces.AuthInterfaces
{
    public interface IAuthService
    {
        Task<Result<LoginResponseDto>> LoginAsync(RequestToken requestToken);
    }
}

