using API.Infrastructure;
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

    public ActiveCenterController(IActiveCenterService centers, AuthCookieService cookies)
    {
        _centers = centers;
        _cookies = cookies;
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
        var tokens = await _centers.SelectAsync(request.CenterId, cancellationToken);
        _cookies.SetAuthCookies(Response, tokens.AccessToken, tokens.RefreshToken);
        return ApiResponse.Success("تم تحديث المركز النشط.", new { tokens.Roles, tokens.Permissions, request.CenterId });
    }
}

public sealed class SelectCenterRequest
{
    public Guid? CenterId { get; set; }
}
