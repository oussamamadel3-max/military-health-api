using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Application.Services.AuthServices;

public sealed class PermissionGate : IPermissionGate
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserPermissionService _permissions;

    public PermissionGate(IHttpContextAccessor httpContextAccessor, IUserPermissionService permissions)
    {
        _httpContextAccessor = httpContextAccessor;
        _permissions = permissions;
    }

    public IReadOnlyCollection<string> GetPermissions()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return Array.Empty<string>();

        var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return Array.Empty<string>();

        return _permissions.GetPermissionNames(userId);
    }

    public bool Has(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission)) return false;
        return GetPermissions().Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasAll(params string[] permissions)
    {
        if (permissions == null || permissions.Length == 0) return true;
        var owned = GetPermissions();
        return permissions.All(p =>
            !string.IsNullOrWhiteSpace(p) &&
            owned.Contains(p, StringComparer.OrdinalIgnoreCase));
    }

    public void EnsureAll(params string[] permissions)
    {
        if (HasAll(permissions)) return;

        var owned = GetPermissions();
        var missing = (permissions ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p) && !owned.Contains(p, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        throw new ForbiddenPermissionException(
            missing.Count == 0
                ? "غير مصرح بتنفيذ هذه العملية."
                : $"تفتقد الصلاحيات التالية: {string.Join(", ", missing)}");
    }
}
