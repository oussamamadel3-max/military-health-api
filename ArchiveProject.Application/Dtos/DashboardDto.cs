namespace ArchiveProject.Application.Dtos.DashboardDtos
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

    public class BranchStatsDto
    {
        public long BranchId { get; set; }      // سنضع DepartmentId هنا حسب الواجهة الأمامية
        public string BranchName { get; set; } = "";
        public long DocumentsCount { get; set; }
    }

    public class DepartmentStatsDto
    {
        public long DepartmentId { get; set; }
        public string DepartmentName { get; set; } = "";
        public long DocumentsCount { get; set; }
    }



    public class DocumentGrowthPointDto
    {
        public string Period { get; set; } = ""; // yyyy-MM
        public long Count { get; set; }
    }

    public class StorageUsageByTypeDto
    {
        public long DocumentTypeId { get; set; }
        public string DocumentTypeName { get; set; } = "";
        public long SizeBytes { get; set; }
        public string? SizeReadable { get; set; }
        public long? DocumentsCount { get; set; }
    }

    public class StorageUsageDto
    {
        public long TotalBytes { get; set; }
        public string? TotalReadable { get; set; }
        public List<StorageUsageByTypeDto>? ByType { get; set; }
    }

    public class TopDocumentTypeDto
    {
        public long DocumentTypeId { get; set; }
        public string DocumentTypeName { get; set; } = "";
        public long DocumentsCount { get; set; }
        public long? TotalSizeBytes { get; set; }
    }

    public class DashboardStatsDto
    {
        public long DocumentsCount { get; set; }
        public long GroupsCount { get; set; }

        public List<RecentActivityDto> RecentActivities { get; set; } = new();
        public List<ActiveUserStatsDto> MostActiveUsers { get; set; } = new();
        public List<BranchStatsDto> BranchesStats { get; set; } = new();
        public List<DepartmentStatsDto> DepartmentsStats { get; set; } = new();
        public List<DocumentGrowthPointDto> DocumentGrowth { get; set; } = new();
        public StorageUsageDto StorageUsage { get; set; } = new();
        public List<TopDocumentTypeDto> TopDocumentTypes { get; set; } = new();
    }
}
