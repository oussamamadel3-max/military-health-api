using ArchiveProject.Application.Common.Interfaces.DashboardInterfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos.DashboardDtos;
using Microsoft.AspNetCore.Mvc;

namespace ArchiveProject.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _service;
        public DashboardController(IDashboardService service) => _service = service;

        [HttpGet]
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
