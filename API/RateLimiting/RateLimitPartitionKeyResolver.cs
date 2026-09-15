using System.Security.Claims;

namespace API.RateLimiting;

public interface IRateLimitPartitionKeyResolver
{
    string ResolveUserOrIp(HttpContext context);
    string ResolveIp(HttpContext context);
}

public sealed class RateLimitPartitionKeyResolver : IRateLimitPartitionKeyResolver
{
    public string ResolveUserOrIp(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var userId =
                context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("sub");

            if (!string.IsNullOrWhiteSpace(userId))
                return $"user:{userId}";
        }

        return ResolveIp(context);
    }

    public string ResolveIp(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(ip) ? "ip:unknown" : $"ip:{ip}";
    }
}
