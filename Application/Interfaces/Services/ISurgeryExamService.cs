using Application.Common.Results;
using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// تسجيل وعرض وفهرسة فحص الجراحة مع تقييم العيادة
/// (FR-036 / شريحة عرض وتعديل وقائمة FR-039 لعيادة الجراحة).
/// </summary>
public interface ISurgeryExamService
{
    /// <summary>
    /// يحفظ تفاصيل الجراحة وتقييم العيادة ضمن قفل التحرير المملوك،
    /// ثم يحرّر القفل بعد النجاح.
    /// </summary>
    Task<SurgeryExamDto> SaveAsync(
        Guid memberId,
        SaveSurgeryExamRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعرض فحص الجراحة الحالي كما أُدخل ضمن نطاق المركز والعيادة المسندة.</summary>
    Task<SurgeryExamDto> GetAsync(
        Guid memberId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعيد قائمة فحوصات الجراحة الحالية مع البحث والترقيم (شريحة قائمة FR-039).</summary>
    Task<PaginatedList<SurgeryExamListItemDto>> ListAsync(
        ClinicExamListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
