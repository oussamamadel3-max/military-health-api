using Domain.Interfaces;

namespace Domain.Entities.Core;

/// <summary>
/// إثبات واحد لاكتمال التحقق من ترحيل السجلات التاريخية (FR-012 / CON-13).
/// وجود صف ناجح يعني أن الجرد لم يُبقِ سجلاً تشغيلياً محلياً بلا مركز صالح، وليس مجرد تنفيذ الإسناد.
/// </summary>
public class HistoricalCenterMigrationCompletion : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TargetCenterId { get; set; }
    public DateTimeOffset VerifiedAt { get; set; }
    public string VerifiedByUserId { get; set; } = null!;
    public string SummaryJson { get; set; } = "[]";
    public int ActiveCenterCountAtVerification { get; set; }
    public bool PreExistingMultiCenterDetected { get; set; }

    public MedicalCenter TargetCenter { get; set; } = null!;
}
