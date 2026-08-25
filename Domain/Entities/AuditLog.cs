namespace Domain.Entities
{
    public class AuditLog
    {
        public long LogId { get; set; }

        public string UserId { get; set; } = null!;

        public string? UserName { get; set; }
        public string? IPAddress { get; set; }

        public string ActionType { get; set; } = null!;

        public string TableName { get; set; } = null!;

        public long RecordId { get; set; }

        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        public string? ChangedFields { get; set; } // قائمة الحقول التي تم تعديلها

        public string? ChangeSummary { get; set; } // ملخص مختصر للتغيير

        // استخدم default SQL Server صحيح
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    }
}
