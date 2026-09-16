namespace Application.Dtos.ReportDtos
{
    public class AuditLogReportRowDto
    {
        public DateTimeOffset Timestamp { get; set; }
        public string UserName { get; set; } = "";
        public string ActionType { get; set; } = "";
        public string TableName { get; set; } = "";
        public long? RecordId { get; set; }
    }
}
