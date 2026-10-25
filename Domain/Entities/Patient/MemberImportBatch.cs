using Domain.Entities.Auth;
using Domain.Entities.Core;

namespace Domain.Entities.Patient;

/// <summary>
/// جلسة استيراد منتسبين من Excel (معاينة ثم اعتماد/إلغاء) دون إنشاء Members في مرحلة المعاينة.
/// </summary>
public class MemberImportBatch
{
    public Guid ImportBatchId { get; set; }
    public Guid CenterId { get; set; }
    public string CreatedByUserId { get; set; } = null!;
    /// <summary>Previewed | Committed | Cancelled</summary>
    public string Status { get; set; } = MemberImportBatchStatuses.Previewed;
    public string FileName { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public int ValidCount { get; set; }
    public int DuplicateCount { get; set; }
    public int InvalidCount { get; set; }
    public string ValidRowsJson { get; set; } = "[]";
    public string ErrorsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public MedicalCenter Center { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}

/// <summary>حالات جلسة استيراد المنتسبين.</summary>
public static class MemberImportBatchStatuses
{
    public const string Previewed = "Previewed";
    public const string Committed = "Committed";
    public const string Cancelled = "Cancelled";
}
