namespace Application.Dtos.DashboardDtos
{
    public class RecentActivityDto
    {
        public long Id { get; set; }
        public string UserId { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Action { get; set; } = "";     // Added/Modified/Deleted/Login...
        public long? RecordId { get; set; }
        public string Timestamp { get; set; } = "";  // ISO
    }

    public class ActiveUserStatsDto
    {
        public string UserId { get; set; } = "";
        public string UserName { get; set; } = "";
        public long ActionsCount { get; set; }
        public string LastActivityAt { get; set; } = "";
    }

    public class DashboardStatsDto
    {
        public List<RecentActivityDto> RecentActivities { get; set; } = new();
        public List<ActiveUserStatsDto> MostActiveUsers { get; set; } = new();
    }

    public class DashboardExportRow
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
