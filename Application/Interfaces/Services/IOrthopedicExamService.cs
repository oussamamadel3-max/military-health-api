using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// تسجيل وعرض فحص العظمية مع تقييم العيادة ضمن عملية واحدة
/// (FR-037 / شريحة عرض وتعديل FR-039 للعيادة العظمية).
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
}
