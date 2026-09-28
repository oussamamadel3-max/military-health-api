using Domain.Entities.Auth;

namespace Application.Interfaces;

public sealed record ActorScope(
    User? Actor,
    string UserId,
    bool IsSuperAdmin,
    IReadOnlyList<string> NormalizedRoleNames);
