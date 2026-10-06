using Application.Common.Security;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Http;

namespace Application.Services.AuthServices;

public class CurrentCenterService : ICurrentCenterService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentCenterService(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public string? UserId =>
        _httpContextAccessor.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value;

    public Guid? CenterId
    {
        get
        {
            var raw = _httpContextAccessor.HttpContext?.User?.FindFirst(CenterClaimTypes.CenterId)?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public bool IsCentralContext
    {
        get
        {
            var scope = _httpContextAccessor.HttpContext?.User?.FindFirst(CenterClaimTypes.Scope)?.Value;
            if (string.Equals(scope, CenterClaimTypes.Center, StringComparison.OrdinalIgnoreCase))
                return false;
            return !CenterId.HasValue || string.Equals(scope, CenterClaimTypes.Central, StringComparison.OrdinalIgnoreCase);
        }
    }
}
