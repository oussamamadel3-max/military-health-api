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
        public async Task<Result<DashboardStatsDto>> Get([FromQuery] DashboardFilter filter)
        {
            filter ??= new DashboardFilter();
            return await _service.GetAsync(filter);
        }
    }
}
