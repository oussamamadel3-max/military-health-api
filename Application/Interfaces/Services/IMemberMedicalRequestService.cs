using Application.Dtos.ClinicWorkspace;

namespace Application.Interfaces.Services;

/// <summary>
/// عرض مجمّع لاستشارات واستقصاءات ومرفقات المنتسب ضمن نطاق المركز (FR-050).
/// </summary>
public interface IMemberMedicalRequestService
{
    /// <summary>
    /// يعيد عقداً موحّداً لكل السجلات المسموح بها للمنتسب في المركز النشط،
    /// مع Empty State وتمييز المعلّق وبيانات المرفقات عند توفر AttachmentsRead.
    /// </summary>
    Task<MemberMedicalRequestsDto> GetForMemberAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
