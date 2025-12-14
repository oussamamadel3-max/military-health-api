using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ArchiveProject.Domain.Entities
{
    [Table("AuditLog")]
    [Index(nameof(TableName), nameof(RecordId), Name = "idx_table_record")]
    [Index(nameof(Timestamp), Name = "idx_timestamp")]
    public class AuditLog
    {
        [Key]
        public long LogId { get; set; }

        public string UserId { get; set; } = null!;

        public string? UserName { get; set; }
        public string? IPAddress { get; set; }

        [StringLength(20)]
        public string ActionType { get; set; } = null!;

        [StringLength(100)]
        [Unicode(false)]
        public string TableName { get; set; } = null!;

        public long RecordId { get; set; }

        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        public string? ChangedFields { get; set; } // قائمة الحقول التي تم تعديلها

        [StringLength(1000)]
        public string? ChangeSummary { get; set; } // ملخص مختصر للتغيير

        // استخدم default SQL Server صحيح
        [Column(TypeName = "datetimeoffset")]
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    }
}
