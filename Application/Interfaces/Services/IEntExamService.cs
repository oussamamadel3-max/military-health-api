using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// تسجيل وعرض فحص الأذنية مع تقييم العيادة ضمن عملية واحدة
/// (FR-038 / شريحة عرض وتعديل FR-039 لعيادة الأذنية).
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
}
