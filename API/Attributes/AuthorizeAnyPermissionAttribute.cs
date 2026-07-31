using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API.Attributes
{
    public class AuthorizeAnyPermissionAttribute : TypeFilterAttribute
    {
        public AuthorizeAnyPermissionAttribute(params string[] permissions)
            : base(typeof(AuthorizeAnyPermissionFilter))
        {
            Arguments = new object[] { permissions };
        }
    }

    public class AuthorizeAnyPermissionFilter : IAsyncAuthorizationFilter
    {
        private readonly string[] _permissions;
        private readonly IUserPermissionService _userPermissionService;

        public AuthorizeAnyPermissionFilter(string[] permissions, IUserPermissionService userPermissionService)
        {
            _permissions = permissions;
            _userPermissionService = userPermissionService;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var userId = context.HttpContext.User?.FindFirst("sub")?.Value;
            if (userId == null)
            {
                throw new UnauthenticatedUserException();
            }

            bool hasAny = false;
            foreach (var perm in _permissions)
            {
                if (_userPermissionService.HasPermission(userId, perm))
                {
                    hasAny = true;
                    break;
                }
            }

            if (!hasAny)
            {
                throw new ForbiddenPermissionException();
            }
        }
    }
}
