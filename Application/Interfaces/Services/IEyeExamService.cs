using Application.Common.Results;
using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// تسجيل وعرض وفهرسة فحص العيون مع الانكسار وتقييم العيادة
/// (FR-033 / FR-034 / شريحة عرض وتعديل وقائمة FR-039 لعيادة العيون).
/// </summary>
public interface IEyeExamService
{
    /// <summary>
    /// يحفظ تفاصيل العيون والانكسار وتقييم العيادة ضمن قفل التحرير المملوك،
    /// ثم يحرّر القفل بعد النجاح.
    /// </summary>
    Task<EyeExamDto> SaveAsync(
        Guid memberId,
        SaveEyeExamRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعرض فحص العيون الحالي والانكسارات كما أُدخلت ضمن نطاق المركز والعيادة المسندة.</summary>
    Task<EyeExamDto> GetAsync(
        Guid memberId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// يعيد قائمة فحوصات العيون الحالية في العيادة ضمن المركز النشط مع البحث والترقيم (شريحة قائمة FR-039).
    /// </summary>
    Task<PaginatedList<EyeExamListItemDto>> ListAsync(
        EyeExamListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
