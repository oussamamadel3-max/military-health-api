using Application.Common.Security;
using Application.Interfaces.Services;

namespace Application.Interfaces.Repositories;

public interface IAccessContextRepository
{
    Task<AccessSnapshot> LoadAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssignedCenterDto>> GetActiveCentersAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> IsActivelyAssignedAsync(string userId, Guid centerId, CancellationToken cancellationToken = default);
}

public sealed class AccessSnapshot
{
    public List<CenterPermissionEvaluator.RoleAssignment> Roles { get; init; } = new();
    public List<CenterPermissionEvaluator.DirectAssignment> Directs { get; init; } = new();
    public List<CenterPermissionEvaluator.RolePermissionLink> Links { get; init; } = new();
}
