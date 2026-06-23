using Application.Interfaces.Services;
using Application.Common.Results;
using Application.Dtos.DashboardDtos;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _service;
        public DashboardController(IDashboardService service) => _service = service;

        [HttpGet]
        [AuthorizePermission("Dashboard.Read")]
        public async Task<ActionResult<Result<DashboardStatsDto>>> Get([FromQuery] DashboardFilter filter)
        {
            filter ??= new DashboardFilter();

            var res = await _service.GetAsync(filter);
            if (!res.Succeeded || res.Value is null)
                return BadRequest(Result<DashboardStatsDto>.Failure(res.Errors, res.Value, res.Warnings));

            return Ok(Result<DashboardStatsDto>.Success(res.Value, res.Warnings));
        }
    }
}
