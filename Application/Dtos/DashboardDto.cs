namespace Application.Dtos.DashboardDtos
{
    public class RecentActivityDto
    {
        public long Id { get; set; }
        public string UserId { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Action { get; set; } = "";     // Added/Modified/Deleted/Login...
        public long? DocumentId { get; set; }
        public string? DocumentTitle { get; set; }
        public string Timestamp { get; set; } = "";  // ISO
    }

    public class ActiveUserStatsDto
    {
        public string UserId { get; set; } = "";
        public string UserName { get; set; } = "";
        public long ActionsCount { get; set; }
        public string LastActivityAt { get; set; } = "";
    }

    public class DocumentGrowthPointDto
    {
        public string Period { get; set; } = ""; // yyyy-MM
        public long Count { get; set; }
    }

    public class StorageUsageDto
    {
        public long TotalBytes { get; set; }
        public string? TotalReadable { get; set; }
    }

    public class DashboardStatsDto
    {
        public long DocumentsCount { get; set; }

        public List<RecentActivityDto> RecentActivities { get; set; } = new();
        public List<ActiveUserStatsDto> MostActiveUsers { get; set; } = new();
        public List<DocumentGrowthPointDto> DocumentGrowth { get; set; } = new();
        public StorageUsageDto StorageUsage { get; set; } = new();
    }

    public class DashboardExportRow
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
