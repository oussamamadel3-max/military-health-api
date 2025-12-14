using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ArchiveProject.API.Attributes
{
    // 1️⃣ تعريف Attribute جديد يقبل مجموعة صلاحيات
    public class AuthorizeAnyPermissionAttribute : TypeFilterAttribute
    {
        public AuthorizeAnyPermissionAttribute(params string[] permissions)
            : base(typeof(AuthorizeAnyPermissionFilter))
        {
            Arguments = new object[] { permissions };
        }
    }

    // 2️⃣ تعريف الفلتر الذي يتحقق من وجود أي صلاحية
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
                context.Result = new ForbidResult();
                return;
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
                context.Result = new ForbidResult();
        }
    }
}

