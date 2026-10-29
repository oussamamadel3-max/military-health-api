using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// تسجيل ومتابعة الاستشارات التخصصية المرتبطة بالمنتسب وعيادة الطبيب والمركز النشط (FR-046).
/// </summary>
public interface IConsultationService
{
    /// <summary>
    /// ينشئ استشارة PENDING أو COMPLETED حسب توفر النتيجة، مع ربط المنتسب والعيادة المسندة والطبيب الطالب.
    /// </summary>
    Task<ConsultationDto> CreateAsync(
        Guid memberId,
        CreateConsultationRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يسجّل نتيجة استشارة معلّقة ويحدّث الحالة والتاريخ والطبيب الاستشاري عند التوفر.</summary>
    Task<ConsultationDto> UpdateResultAsync(
        Guid consultationId,
        UpdateConsultationResultRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعيد استشارة ضمن المركز النشط وعيادة الطبيب المسندة.</summary>
    Task<ConsultationDto> GetByIdAsync(
        Guid consultationId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعيد استشارات المنتسب في المركز النشط، مع تصفية اختيارية بعيادة مسندة.</summary>
    Task<IReadOnlyList<ConsultationDto>> ListByMemberAsync(
        Guid memberId,
        Guid? clinicId = null,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
