using Application.Dtos.Centers;

namespace Application.Interfaces.Services;

/// <summary>
/// ترحيل السجلات التاريخية إلى مركز معتمد ثم إثبات التحقق قبل السماح بمركز نشط ثانٍ (FR-012).
/// </summary>
public interface IHistoricalCenterMigrationService
{
    Task<HistoricalMigrationReportDto> PreviewAsync(CancellationToken cancellationToken = default);

    Task<HistoricalMigrationReportDto> ExecuteAsync(Guid targetCenterId, CancellationToken cancellationToken = default);

    Task<HistoricalMigrationStatusDto> VerifyAsync(Guid? targetCenterId, CancellationToken cancellationToken = default);

    Task<HistoricalMigrationStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// يمنع إنشاء أو تفعيل مركز نشط إضافي ما لم يوجد إثبات تحقق. المركز النشط الأول يبقى مسموحاً.
    /// </summary>
    Task EnsureAdditionalActiveCenterAllowedAsync(CancellationToken cancellationToken = default);
}
