using API.Infrastructure;
using Application.Dtos.DashboardDtos;
using Application.Interfaces.Services;
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
        public async Task<IResult> Get([FromQuery] DashboardFilter filter)
        {
            filter ??= new DashboardFilter();
            var stats = await _service.GetAsync(filter);
            return ApiResponse.Success("تم جلب إحصائيات لوحة التحكم بنجاح.", stats);
        }
    }
}
