using Application.Common.Results;
using System.ComponentModel.DataAnnotations;

namespace Application.ModelsDto.AuditLogDtos
{
    // DTO لعرض سجلات التدقيق في القائمة (شاملة المعلومات الأساسية)
    public class AuditLogListDto
    {
        public long LogId { get; set; }
        public string UserId { get; set; } = null!;
        public string UserName { get; set; } = null!;
        public string ActionType { get; set; } = null!;
        public string TableName { get; set; } = null!;
        public string? EntityId { get; set; }
        public Guid? CenterId { get; set; }
        public string? Result { get; set; }
        public string? ContextScope { get; set; }

        public string? ChangeSummary { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? IPAddress { get; set; }

    }

    // DTO لتفاصيل سجل تدقيق واحد (يرث من DTO القائمة ويضيف التفاصيل الكبيرة)
    public class AuditLogDetailsDto : AuditLogListDto
    {
        // حقول التفاصيل الإضافية التي قد تكون كبيرة (لا داعي لتحميلها في القائمة)
        public string? ChangedFields { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }

    public class CreateAuditLogDto
    {
        public string UserId { get; set; }
        public string ActionType { get; set; } = null!;
        public string TableName { get; set; } = null!;
        public string? EntityId { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? UserName { get; set; }         // اختياري
        public string? IPAddress { get; set; }        // اختياري
    }

    public class AuditLogCountsDto
    {
        public long Total { get; set; }
        public long Added { get; set; }
        public long Modified { get; set; }
        public long Deleted { get; set; }
    }


    public enum AuditActionType
    {
        Added = 1,
        Modified = 2,
        Deleted = 3
    }

    public class AuditLogSummaryDto
    {
        public long Total { get; set; }
        public long Added { get; set; }
        public long Modified { get; set; }
        public long Deleted { get; set; }
    }
    public class AuditLogFilter : IValidatableObject
    {
        [StringLength(200)]
        public string? searchUserName { get; set; }

        public DateTimeOffset? From { get; set; }
        public DateTimeOffset? To { get; set; }

        [StringLength(200)]
        public string? userName { get; set; }

        public AuditActionType? actionType { get; set; }

        [MaxLength(100)]
        public List<string>? tableNames { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (From.HasValue && To.HasValue && From > To)
                yield return new ValidationResult("تاريخ البداية يجب ألا يتعدى تاريخ النهاية.", new[] { nameof(From), nameof(To) });
        }
    }
}
