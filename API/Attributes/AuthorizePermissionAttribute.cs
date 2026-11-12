using Application.Common.Security;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class AuthorizePermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _requiredPermissions;

    public AuthorizePermissionAttribute(params string[] requiredPermissions)
    {
        _requiredPermissions = requiredPermissions ?? Array.Empty<string>();
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user?.Identity == null || !user.Identity.IsAuthenticated)
        {
            throw new UnauthenticatedUserException();
        }

        if (_requiredPermissions.Length == 0)
            return;

        var gate = context.HttpContext.RequestServices.GetService<IPermissionGate>();
        if (gate != null)
        {
            gate.EnsureAll(_requiredPermissions);
            return;
        }

        var permissions = user.Claims
            .Where(c => c.Type == "permissions")
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!_requiredPermissions.All(p => permissions.Contains(p)))
        {
            var required = string.Join(",", _requiredPermissions);
            if (IamSensitiveAccessDenial.RequiresAuditForPermission(required))
            {
                throw ForbiddenPermissionException.AccessDenied(
                    "ليس لديك الصلاحية المطلوبة.",
                    "AuthorizePermissionAttribute",
                    "permission-missing",
                    permission: required);
            }

            throw new ForbiddenPermissionException("ليس لديك الصلاحية المطلوبة.");
        }
    }
}
