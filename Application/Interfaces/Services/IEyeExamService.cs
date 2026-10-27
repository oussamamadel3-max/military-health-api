using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// تسجيل وعرض فحص العيون مع الانكسار وتقييم العيادة ضمن عملية واحدة
/// (FR-033 / FR-034 / شريحة عرض وتعديل FR-039 لعيادة العيون).
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
}
