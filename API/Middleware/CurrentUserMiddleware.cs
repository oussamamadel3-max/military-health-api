using Application.Interfaces.Services;
using System.Net;
using System.Security.Claims;

namespace API.Middleware
{
    public class CurrentUserMiddleware
    {
        private readonly RequestDelegate _next;

        public CurrentUserMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ICurrentUserService currentUser)
        {
            var clientIp = GetClientIp(context) ?? "unknown";
            var correlationHeader = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
            var correlationId = Guid.TryParse(correlationHeader, out var parsed) ? parsed : Guid.NewGuid();
            currentUser.SetRequest(context.Request.Headers.UserAgent.ToString(), correlationId);

            if (context.User.Identity?.IsAuthenticated ?? false)
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? context.User.FindFirst("sub")?.Value
                             ?? "0";

                var userName = context.User.FindFirst(ClaimTypes.Name)?.Value
                               ?? context.User.FindFirst("unique_name")?.Value
                               ?? context.User.Identity?.Name
                               ?? "Unknown";

                currentUser.SetUser(userId, userName, clientIp);
            }
            else
            {
                currentUser.SetUser("0", "Anonymous", clientIp);
            }

            await _next(context);
        }

        private static string? GetClientIp(HttpContext context)
        {
            // يعتمد على UseForwardedHeaders + KnownProxies فقط — لا تُقرأ الهيدرات يدوياً.
            var remote = context.Connection.RemoteIpAddress;
            if (remote == null) return null;

            if (remote.IsIPv4MappedToIPv6)
                return remote.MapToIPv4().ToString();

            if (IPAddress.IsLoopback(remote))
                return "127.0.0.1";

            return remote.ToString();
        }
    }
}
