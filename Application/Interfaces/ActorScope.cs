using Domain.Entities.Auth;

namespace Application.Interfaces;

public sealed record ActorScope(
    User? Actor,
    string UserId,
    bool IsSuperAdmin,
    bool IsBranchManager,
    IReadOnlyList<string> NormalizedRoleNames);
