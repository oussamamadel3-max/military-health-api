namespace ArchiveProject.Application.Dtos.ReportDtos
{
    public class DocumentReportRowDto
    {
        public long DocumentNumber { get; set; }
        public string Title { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = "";
    }

    public class AuditLogReportRowDto
    {
        public DateTimeOffset Timestamp { get; set; }
        public string UserName { get; set; } = "";
        public string ActionType { get; set; } = "";
        public string TableName { get; set; } = "";
        public long? DocumentId { get; set; }
    }
}
