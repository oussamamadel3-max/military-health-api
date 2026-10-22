using System.Security.Claims;
using API.Infrastructure;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.AuthControllers;

[ApiController]
[Authorize]
[Route("api/centers")]
public class ActiveCenterController : ControllerBase
{
    private readonly IActiveCenterService _centers;
    private readonly AuthCookieService _cookies;
    private readonly ICsrfService _csrf;

    public ActiveCenterController(IActiveCenterService centers, AuthCookieService cookies, ICsrfService csrf)
    {
        _centers = centers;
        _cookies = cookies;
        _csrf = csrf;
    }

    [HttpGet("mine")]
    public async Task<IResult> Mine(CancellationToken cancellationToken)
    {
        var centers = await _centers.GetMyCentersAsync(cancellationToken);
        return ApiResponse.Success("تم جلب المراكز المسندة.", centers);
    }

    [HttpPost("select")]
    public async Task<IResult> Select([FromBody] SelectCenterRequest request, CancellationToken cancellationToken)
    {
        var tokens = await _centers.SelectAsync(
            request.CenterId,
            _cookies.TryGetRefreshToken(Request),
            cancellationToken);
        _cookies.SetAuthCookies(Response, tokens.AccessToken, tokens.RefreshToken);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var csrfToken = string.Empty;
        if (!string.IsNullOrWhiteSpace(userId))
            _csrf.IssueCsrf(Response, userId, out csrfToken);
        return ApiResponse.Success("تم تحديث المركز النشط.", new LoginResponseDto { CsrfToken = csrfToken });
    }
}

public sealed class SelectCenterRequest
{
    public Guid? CenterId { get; set; }
}
