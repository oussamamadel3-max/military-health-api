using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces.Repositories;

namespace Application.Common.Security;

/// <summary>
/// Ensures a user retains at least one globally active role association after role mutations.
/// An active role means: association IsActive, role entity IsActive, and not revoked via IamRetention.
/// Direct UserPermission grants never substitute for this minimum.
/// Independent from <see cref="Permissions.IamAdminGuard"/>.
/// </summary>
public static class MinimumActiveRoleGuard
{
    /// <summary>DTO/request has no usable role token.</summary>
    public const string AtLeastOneRoleMessage = "يجب اختيار دور واحد على الأقل.";

    /// <summary>Projected or persisted outcome would leave the user with zero active roles.</summary>
    public const string MustRetainOneRoleMessage = "يجب أن يحتفظ المستخدم بدور فعّال واحد على الأقل.";

    /// <summary>Identity of one active role assignment (role + center/central scope).</summary>
    public readonly record struct ActiveRoleKey(string RoleId, Guid? CenterId);

    /// <summary>
    /// Rejects requests that do not name any non-empty role id/name after trim.
    /// Used by AssignRolesDto paths and empty Patch/Update role lists.
    /// </summary>
    public static void EnsureRequestContainsRoleToken(
        IEnumerable<string?>? roleIds,
        IEnumerable<string?>? roleNames = null)
    {
        if (CollectTokens(roleIds, roleNames).Count == 0)
            throw new InvalidUserDataException(AtLeastOneRoleMessage);
    }

    /// <summary>
    /// Patch contract: <c>roles: null</c> means leave roles unchanged;
    /// <c>roles: []</c> (or only blank tokens) is an invalid clear request.
    /// </summary>
    public static void EnsurePatchRolesNotEmptyClear(
        IReadOnlyCollection<RoleLiteDto>? roles)
    {
        if (roles is null)
            return;

        var tokens = roles
            .Where(r => r is not null)
            .Select(r => !string.IsNullOrWhiteSpace(r.RoleId) ? r.RoleId! : r.RoleName)
            .Where(s => !string.IsNullOrWhiteSpace(s));

        if (!tokens.Any())
            throw new InvalidUserDataException(AtLeastOneRoleMessage);
    }

    /// <summary>
    /// Computes remaining active role keys after a scoped sync and rejects an empty result.
    /// Counts associations across all centers and the central scope, not only the edited scope.
    /// </summary>
    public static void EnsureProjectedActiveRolesRemain(
        IEnumerable<ActiveRoleKey> currentActive,
        IEnumerable<ActiveRoleKey> removing,
        IEnumerable<ActiveRoleKey> adding)
    {
        var remaining = currentActive
            .Select(Normalize)
            .ToHashSet();

        foreach (var key in removing)
            remaining.Remove(Normalize(key));

        foreach (var key in adding)
            remaining.Add(Normalize(key));

        if (remaining.Count == 0)
            throw new InvalidUserDataException(MustRetainOneRoleMessage);
    }

    /// <summary>
    /// Re-reads persisted active role ids (association + role both active) after SaveChanges,
    /// catching concurrent removals of the last two roles before commit/session invalidation.
    /// </summary>
    public static async Task EnsurePersistedActiveRoleRemainsAsync(
        IUserRepository userRepository,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var activeRoleIds = await userRepository.GetActiveRoleIdsAsync(userId, cancellationToken);
        if (activeRoleIds.Count == 0)
            throw new InvalidUserDataException(MustRetainOneRoleMessage);
    }

    /// <summary>Normalizes and merges RoleIds + RoleNames with case-insensitive de-duplication.</summary>
    public static List<string> CollectTokens(
        IEnumerable<string?>? roleIds,
        IEnumerable<string?>? roleNames = null)
    {
        var tokens = new List<string>();
        if (roleIds is not null)
            tokens.AddRange(roleIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        if (roleNames is not null)
            tokens.AddRange(roleNames.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

        return tokens
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ActiveRoleKey Normalize(ActiveRoleKey key) =>
        new(key.RoleId.Trim().ToUpperInvariant(), key.CenterId);
}
