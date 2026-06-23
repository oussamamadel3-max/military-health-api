namespace Application.Interfaces;

public interface IActorScopeProvider
{
    Task<ActorScope> GetScopeAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetNormalizedRoleNamesAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> IsSuperAdminAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> IsBranchManagerAsync(string userId, CancellationToken cancellationToken = default);
}
