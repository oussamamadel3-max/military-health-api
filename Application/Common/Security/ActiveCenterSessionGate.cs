using System.Security.Claims;

namespace Application.Common.Security;

public static class ActiveCenterSessionGate
{
    public static Guid? ResolveClaimedCenter(Guid? claimedCenterId, bool activelyAssigned)
        => claimedCenterId is Guid id && activelyAssigned ? id : null;

    public static void RetainUnscopedContext(
        ClaimsIdentity identity,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> roles)
    {
        Remove(identity, CenterClaimTypes.CenterId);
        Remove(identity, CenterClaimTypes.Scope);
        Remove(identity, "permissions");
        Remove(identity, "role");
        Remove(identity, ClaimTypes.Role);

        identity.AddClaim(new Claim(CenterClaimTypes.Scope, CenterClaimTypes.Center));

        foreach (var permission in permissions.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            identity.AddClaim(new Claim("permissions", permission));

        foreach (var role in roles.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            identity.AddClaim(new Claim("role", role));
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }
    }

    private static void Remove(ClaimsIdentity identity, string type)
    {
        foreach (var claim in identity.FindAll(type).ToList())
            identity.RemoveClaim(claim);
    }
}
