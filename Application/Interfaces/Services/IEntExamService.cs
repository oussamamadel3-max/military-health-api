using Application.Common.Results;
using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// تسجيل وعرض وفهرسة فحص الأذنية مع تقييم العيادة
/// (FR-038 / شريحة عرض وتعديل وقائمة FR-039 لعيادة الأذنية).
/// </summary>
public interface IEntExamService
{
    /// <summary>
    /// يحفظ تفاصيل الأذنية وتقييم العيادة ضمن قفل التحرير المملوك،
    /// ثم يحرّر القفل بعد النجاح.
    /// </summary>
    Task<EntExamDto> SaveAsync(
        Guid memberId,
        SaveEntExamRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعرض فحص الأذنية الحالي كما أُدخل ضمن نطاق المركز والعيادة المسندة.</summary>
    Task<EntExamDto> GetAsync(
        Guid memberId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعيد قائمة فحوصات الأذنية الحالية مع البحث والترقيم (شريحة قائمة FR-039).</summary>
    Task<PaginatedList<EntExamListItemDto>> ListAsync(
        ClinicExamListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
