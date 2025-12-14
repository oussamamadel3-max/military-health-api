using Microsoft.AspNetCore.Mvc;
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
            context.Result = new UnauthorizedResult();
            return;
        }

        var permissions = user.Claims
            .Where(c => c.Type == "permissions")
            .Select(c => c.Value)
            .ToList();

        // التحقق إذا كان الـ User يملك البرمشن المطلوب
        if (!permissions.Contains(_requiredPermission))
        {
            context.Result = new ForbidResult();
        }
    }
}