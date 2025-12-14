using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using System.Net;
using System.Security.Claims;

namespace ArchiveProject.API.Middleware
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
            var clientIp = GetClientIp(context);

            if (context.User.Identity?.IsAuthenticated ?? false)
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? context.User.FindFirst("sub")?.Value
                             ?? "0";

                var userName = context.User.FindFirst(ClaimTypes.Name)?.Value
                               ?? context.User.FindFirst("unique_name")?.Value
                               ?? context.User.Identity?.Name
                               ?? "Unknown";

                // ملاحظة: تأكد أن ICurrentUserService.SetUser يقبل ipAddress كـ باراميتر ثالث
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
            // 1) إذا في بروكسي، X-Forwarded-For بيكون "ip1, ip2, ip3"
            if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var xff) &&
                !string.IsNullOrWhiteSpace(xff))
            {
                // أول IP عادة هو IP العميل
                var first = xff.ToString().Split(',')[0].Trim();
                if (IsValidIp(first))
                    return NormalizeIp(first);
            }

            // 2) بعض البروكسيات تستخدم X-Real-IP
            if (context.Request.Headers.TryGetValue("X-Real-IP", out var xri) &&
                !string.IsNullOrWhiteSpace(xri))
            {
                var ip = xri.ToString().Trim();
                if (IsValidIp(ip))
                    return NormalizeIp(ip);
            }

            // 3) fallback: Kestrel
            var remote = context.Connection.RemoteIpAddress?.ToString();
            return NormalizeIp(remote);
        }

        private static bool IsValidIp(string? ip)
            => !string.IsNullOrWhiteSpace(ip) && IPAddress.TryParse(ip, out _);

        private static string? NormalizeIp(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return null;

            // إزالة IPv6-mapped IPv4 مثل ::ffff:192.168.1.10
            if (IPAddress.TryParse(ip, out var addr))
            {
                if (addr.IsIPv4MappedToIPv6) return addr.MapToIPv4().ToString();

                // توحيد ::1 إلى 127.0.0.1
                if (IPAddress.IsLoopback(addr))
                    return addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                         ? "127.0.0.1"
                         : "127.0.0.1";
            }

            return ip;
        }
    }
}

