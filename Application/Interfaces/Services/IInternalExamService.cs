using Application.Common.Results;
using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// تسجيل وعرض وفهرسة فحص الباطنة مع تقييم العيادة
/// (FR-035 / شريحة عرض وتعديل وقائمة FR-039 لعيادة الباطنة).
/// </summary>
public interface IInternalExamService
{
    /// <summary>
    /// يحفظ تفاصيل الباطنة وتقييم العيادة ضمن قفل التحرير المملوك،
    /// ثم يحرّر القفل بعد النجاح.
    /// </summary>
    Task<InternalExamDto> SaveAsync(
        Guid memberId,
        SaveInternalExamRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعرض فحص الباطنة الحالي كما أُدخل ضمن نطاق المركز والعيادة المسندة.</summary>
    Task<InternalExamDto> GetAsync(
        Guid memberId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعيد قائمة فحوصات الباطنة الحالية مع البحث والترقيم (شريحة قائمة FR-039).</summary>
    Task<PaginatedList<InternalExamListItemDto>> ListAsync(
        ClinicExamListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
