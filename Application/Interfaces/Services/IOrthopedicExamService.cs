using Application.Common.Results;
using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// تسجيل وعرض وفهرسة فحص العظمية مع تقييم العيادة
/// (FR-037 / شريحة عرض وتعديل وقائمة FR-039 للعيادة العظمية).
/// </summary>
public interface IOrthopedicExamService
{
    /// <summary>
    /// يحفظ تفاصيل العظمية وتقييم العيادة ضمن قفل التحرير المملوك،
    /// ثم يحرّر القفل بعد النجاح.
    /// </summary>
    Task<OrthopedicExamDto> SaveAsync(
        Guid memberId,
        SaveOrthopedicExamRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعرض فحص العظمية الحالي كما أُدخل ضمن نطاق المركز والعيادة المسندة.</summary>
    Task<OrthopedicExamDto> GetAsync(
        Guid memberId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعيد قائمة فحوصات العظمية الحالية مع البحث والترقيم (شريحة قائمة FR-039).</summary>
    Task<PaginatedList<OrthopedicExamListItemDto>> ListAsync(
        ClinicExamListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
