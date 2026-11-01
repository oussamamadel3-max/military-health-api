using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// عقد مجمّع لاستشارات واستقصاءات ومرفقات المنتسب ضمن نطاق المركز (FR-050).
/// </summary>
public interface IMemberMedicalRequestService
{
    /// <summary>
    /// يعيد عقداً موحّداً لكل السجلات المسموح بها للمنتسب في المركز النشط،
    /// مع Empty State وتمييز المعلّق وبيانات المرفقات عند توفر AttachmentsRead.
    /// يفرض صلاحية <c>Medical.ConsultationsInvestigations</c>.
    /// </summary>
    Task<MemberMedicalRequestsDto> GetForMemberAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// قراءة مجمّعة بعد أن يكون المستدعي قد تحقق من المركز والنطاق والصلاحية المناسبة
    /// (مثل <c>Clinical.FinalDecision</c> لمسار المشرف). لا تعيد فرض ConsultationsInvestigations.
    /// تبقى AttachmentsRead اختيارية لتضمين المرفقات.
    /// </summary>
    Task<MemberMedicalRequestsDto> GetForMemberInAuthorizedCenterAsync(
        Guid memberId,
        Guid authorizedCenterId,
        CancellationToken cancellationToken = default);
}
