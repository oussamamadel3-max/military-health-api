using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services;

public interface IActiveCenterService
{
    Task<IReadOnlyList<AssignedCenterDto>> GetMyCentersAsync(CancellationToken cancellationToken = default);
    Task<TokenGenerationResultDto> SelectAsync(Guid? centerId, string? currentRefreshToken = null, CancellationToken cancellationToken = default);
    Task<Guid> RequireOperationalCenterAsync(CancellationToken cancellationToken = default);
}

public sealed class AssignedCenterDto
{
    public Guid CenterId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
}
