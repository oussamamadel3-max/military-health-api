using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// تسجيل ومتابعة الاستقصاءات المخبرية والشعاعية مع ربط اختياري بإجراء مؤجّل دون إغلاق تلقائي (FR-047).
/// </summary>
public interface IInvestigationService
{
    /// <summary>
    /// ينشئ استقصاء PENDING أو COMPLETED حسب توفر النتيجة، مع ربط المنتسب والعيادة المسندة والطبيب الطالب.
    /// </summary>
    Task<InvestigationDto> CreateAsync(
        Guid memberId,
        CreateInvestigationRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يسجّل نتيجة استقصاء معلّق ويحدّث الحالة وresultedAt دون إغلاق الإجراء المؤجّل المرتبط.</summary>
    Task<InvestigationDto> UpdateResultAsync(
        Guid investigationId,
        UpdateInvestigationResultRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعيد استقصاء ضمن المركز النشط وعيادة الطبيب المسندة.</summary>
    Task<InvestigationDto> GetByIdAsync(
        Guid investigationId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعيد استقصاءات المنتسب في المركز النشط، مع تصفية اختيارية بالعيادة والحالة.</summary>
    Task<IReadOnlyList<InvestigationDto>> ListByMemberAsync(
        Guid memberId,
        Guid? clinicId = null,
        string? status = null,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعيد الاستقصاءات المعلّقة في عيادة مسندة ضمن المركز النشط لمتابعة الطلبات.</summary>
    Task<IReadOnlyList<InvestigationDto>> ListPendingAsync(
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}