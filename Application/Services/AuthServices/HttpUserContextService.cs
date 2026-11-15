using Application.Interfaces.Services;
using Domain.Enums;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Application.Services.AuthServices;

/// <summary>
/// يستخرج هوية المستخدم ومعرّفات/أسماء الأدوار وصلاحيات الحقول من JWT.
/// </summary>
public class HttpUserContextService : IUserContextService
{
    public const string RoleIdClaimType = "role_id";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private List<string>? _roleIds;
    private List<string>? _roleNames;
    private Dictionary<string, FieldAccessType>? _fieldPermissions;

    public HttpUserContextService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public string? UserId =>
        User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
        User?.FindFirst("sub")?.Value;

    public IReadOnlyList<string> RoleIds => _roleIds ??= ExtractRoleIds();

    public IReadOnlyList<string> RoleNames => _roleNames ??= ExtractRoleNames();

    public IReadOnlyDictionary<string, FieldAccessType> FieldPermissions =>
        _fieldPermissions ??= ExtractFieldPermissions();

    private List<string> ExtractRoleIds()
    {
        return User?.Claims
                   .Where(c => c.Type == RoleIdClaimType)
                   .Select(c => c.Value)
                   .Where(v => !string.IsNullOrWhiteSpace(v))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList() ?? new List<string>();
    }

    private List<string> ExtractRoleNames()
    {
        return User?.Claims
                   .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                   .Select(c => c.Value)
                   .Where(v => !string.IsNullOrWhiteSpace(v))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList() ?? new List<string>();
    }

    private Dictionary<string, FieldAccessType> ExtractFieldPermissions()
    {
        var permissions = new Dictionary<string, FieldAccessType>(StringComparer.OrdinalIgnoreCase);

        if (User == null)
            return permissions;

        foreach (var claim in User.Claims.Where(c => c.Type == "field_permission"))
        {
            var parts = claim.Value.Split(':');
            if (parts.Length != 3)
                continue;

            var entity = parts[0];
            var field = parts[1];
            var access = parts[2];

            if (string.IsNullOrWhiteSpace(entity) || string.IsNullOrWhiteSpace(field))
                continue;

            if (!Enum.TryParse(access, out FieldAccessType accessType))
                continue;

            permissions[$"{entity}:{field}"] = accessType;
        }

        return permissions;
    }
}
