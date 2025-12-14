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
        // e.g. /api/dashboard?from=2025-08-01&to=2025-12-31&branchId=1&recentCount=10
        public async Task<Result<DashboardStatsDto>> Get(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] long? branchId,
            [FromQuery] long? departmentId,
            [FromQuery] int recentCount = 10,
            [FromQuery] int topUsersCount = 5,
            [FromQuery] int growthMonths = 6)
        {
            var filter = new DashboardFilter
            {
                From = from,
                To = to,
                BranchId = branchId,
                DepartmentId = departmentId,
                RecentCount = recentCount,
                TopUsersCount = topUsersCount,
                GrowthMonths = growthMonths
            };

            var res = await _service.GetAsync(filter);
            if (!res.Succeeded || res.Value is null)
                return Result<DashboardStatsDto>.Failure(res.Errors, res.Value, res.Warnings);

            return Result<DashboardStatsDto>.Success(res.Value, res.Warnings);
        }
    }
}
