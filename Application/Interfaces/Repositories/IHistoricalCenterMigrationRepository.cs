using Domain.Entities.Core;

namespace Application.Interfaces.Repositories;

/// <summary>
/// جرد السجلات التشغيلية المحلية ذات مرجع مركز فارغ أو غير موجود، وإسناد ما يمكن إصلاحه.
/// سجلات النقل التاريخية غير القابلة للنسبة الآمنة تُحسب ولا تُعاد كتابتها.
/// </summary>
public interface IHistoricalCenterMigrationRepository
{
    Task<int> CountActiveCentersAsync(CancellationToken cancellationToken = default);

    Task<HistoricalCenterMigrationCompletion?> GetLatestCompletionAsync(CancellationToken cancellationToken = default);

    Task<bool> IsActiveCenterAsync(Guid centerId, CancellationToken cancellationToken = default);

    Task<Guid?> GetSoleActiveCenterIdAsync(CancellationToken cancellationToken = default);

    Task<HistoricalMigrationScan> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// يُسند الأيتام القابلة للإصلاح إلى المركز الهدف داخل وحدة العمل الحالية دون حفظ.
    /// إن وُجدت سجلات نقل غير قابلة للإسناد يُعاد الجرد دون أي تعديل.
    /// </summary>
    Task<HistoricalMigrationScan> AssignRepairableAsync(Guid targetCenterId, CancellationToken cancellationToken = default);

    void AddCompletion(HistoricalCenterMigrationCompletion completion);

    /// <summary>يسقط تعديلات المتعقب بعد التراجع حتى لا تبقى كيانات متّسخة في نفس السياق.</summary>
    void DiscardPendingChanges();
}

/// <summary>نتيجة جرد أو إسناد لنوع كيان واحد.</summary>
public sealed class HistoricalMigrationSlice
{
    public required string EntityName { get; init; }
    public required string LabelAr { get; init; }
    public bool Unassignable { get; init; }
    public string? BlockReason { get; init; }
    public int OrphanCount { get; init; }
    public int AssignedCount { get; init; }
    public IReadOnlyList<Guid> SampleIds { get; init; } = [];
}

/// <summary>جرد مجمّع قبل الترحيل أو بعده.</summary>
public sealed class HistoricalMigrationScan
{
    public IReadOnlyList<HistoricalMigrationSlice> Slices { get; init; } = [];

    public bool HasUnassignable => Slices.Any(s => s.Unassignable && s.OrphanCount > 0);

    public int TotalOrphans => Slices.Sum(s => s.OrphanCount);

    public int TotalAssigned => Slices.Sum(s => s.AssignedCount);

    public int TotalUnassignable => Slices.Where(s => s.Unassignable).Sum(s => s.OrphanCount);
}
