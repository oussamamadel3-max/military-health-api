namespace Application.Dtos.ReportDtos
{
    /// <summary>
    /// ترتيب الخصائص يحدد ترتيب الأعمدة في التصدير مع RTL:
    /// الخاصية الأولى تظهر في أقصى اليمين.
    /// </summary>
    public class AuditLogReportRowDto
    {
        /// <summary>رقم سجل التدقيق (LogId) — ليس تجزئة Guid للكيان.</summary>
        public string LogId { get; set; } = "";

        public string TableName { get; set; } = "";
        public string ActionType { get; set; } = "";
        public string UserName { get; set; } = "";

        /// <summary>اسم السجل من لقطة الحدث، أو «—» عند غيابها. ليس معرّف الكيان.</summary>
        public string? EntityDisplayName { get; set; }

        /// <summary>اسم المركز الحالي، وليس بالضرورة الاسم وقت وقوع الحدث.</summary>
        public string? CenterName { get; set; }

        public string? Result { get; set; }
        public string? ChangeSummary { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }
}
