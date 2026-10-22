using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services;

public interface IUserEffectiveAccessService
{
    Task<UserEffectiveAccessDto> GetEffectiveAccessAsync(
        string userId,
        Guid? centerId = null,
        CancellationToken cancellationToken = default);
}
