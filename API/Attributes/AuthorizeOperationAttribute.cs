using Application.Common.Security;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class AuthorizeOperationAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _permission;
    private readonly OperationScope _scope;

    public AuthorizeOperationAttribute(string permission, OperationScope scope)
    {
        _permission = permission;
        _scope = scope;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
            throw new UnauthenticatedUserException();

        var gate = context.HttpContext.RequestServices.GetRequiredService<IPermissionGate>();
        if (_scope == OperationScope.Central)
        {
            await gate.EnsureCentralAsync(_permission, context.HttpContext.RequestAborted);
            return;
        }

        await gate.EnsureLocalAsync(
            _permission,
            ClientCenterBinding.Read(context.HttpContext.Request),
            context.HttpContext.RequestAborted);
    }
}
