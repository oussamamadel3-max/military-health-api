using Application.Exceptions;
using Microsoft.AspNetCore.Mvc.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class AuthorizePermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _requiredPermission;

    public AuthorizePermissionAttribute(string requiredPermission)
    {
        _requiredPermission = requiredPermission;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user?.Identity == null || !user.Identity.IsAuthenticated)
        {
            throw new UnauthenticatedUserException();
        }

        var permissions = user.Claims
            .Where(c => c.Type == "permissions")
            .Select(c => c.Value)
            .ToList();

        if (!permissions.Contains(_requiredPermission))
        {
            throw new ForbiddenPermissionException();
        }
    }
}
