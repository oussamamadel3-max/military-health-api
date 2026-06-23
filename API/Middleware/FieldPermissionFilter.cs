using Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace API.Middleware
{
    public class FieldPermissionFilter : IAsyncActionFilter
    {
        private readonly IFieldPermissionService _fieldPermissionService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public FieldPermissionFilter(IFieldPermissionService fieldPermissionService,
                             IHttpContextAccessor httpContextAccessor)
        {
            _fieldPermissionService = fieldPermissionService;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            var userId = _httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userRoles = _httpContextAccessor.HttpContext.User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            // التحقق من الصلاحيات هنا

            await next();
        }
    }
}

