using ArchiveProject.Application.Dtos.DashboardDtos;

namespace ArchiveProject.Application.Common.Interfaces.DashboardInterfaces
{
    public class DashboardFilter
    {
        public DateTime? From { get; set; }      // لفلاتر growth أو recent
        public DateTime? To { get; set; }
        public int RecentCount { get; set; } = 10;
        public int TopUsersCount { get; set; } = 5;
        public int GrowthMonths { get; set; } = 6; // آخر N أشهر
    }

    public interface IDashboardService
    {
        Task<Result<DashboardStatsDto>> GetAsync(DashboardFilter filter);
    }
}
