using API.Infrastructure;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.Core;

[ApiController, Route("api/core/me"), Authorize]
public class MeController : ControllerBase
{
    private readonly IUserOrgScopeService _service;
    public MeController(IUserOrgScopeService service) => _service = service;

    [HttpGet("org-scopes")]
    public async Task<IResult> GetMyScopes() =>
        ApiResponse.Success("تم جلب نطاقاتك التنظيمية.", await _service.GetMyScopesAsync());

    [HttpGet("accessible-org-units")]
    public async Task<IResult> GetAccessibleOrgUnits([FromQuery] string permissionName) =>
        ApiResponse.Success("تم جلب الوحدات التنظيمية المتاحة.", await _service.GetMyAccessibleOrgUnitsAsync(permissionName));
}
