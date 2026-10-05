using Domain.Entities.Auth;

namespace Application.Interfaces;

public sealed record ActorScope(
    User? Actor,
    string UserId,
    IReadOnlyList<string> NormalizedRoleNames);
