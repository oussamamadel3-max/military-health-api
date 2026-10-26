using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// محرك مشترك لحفظ تقييم العيادة والتأجيل وفرض قفل التحرير ومنع الفحص الأساسي المكرر
/// (FR-040 / FR-041 / شريحة كتابة FR-045).
/// </summary>
public interface IClinicAssessmentService
{
    /// <summary>
    /// يحفظ هيكل الفحص الأساسي وتقييم العيادة (وإن لزم DeferredAction) ضمن قفل التحرير المملوك،
    /// ثم يحرّر القفل بعد النجاح.
    /// </summary>
    Task<ClinicAssessmentSavedDto> SaveAsync(
        Guid memberId,
        SaveClinicAssessmentRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يحرّر قفل التحرير الذي يملكه الطبيب الحالي دون حفظ تقييم.</summary>
    Task ReleaseEditLockAsync(
        Guid memberId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
