using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// تسجيل وعرض فحص الباطنة مع تقييم العيادة ضمن عملية واحدة
/// (FR-035 / شريحة عرض وتعديل FR-039 لعيادة الباطنة).
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
}
