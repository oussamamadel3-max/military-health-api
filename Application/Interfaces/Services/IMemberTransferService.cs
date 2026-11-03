using Application.Dtos.Members;

namespace Application.Interfaces.Services;

/// <summary>
/// نقل ملف المنتسب بين المراكز مع إغلاق/فتح الهوية المحلية وحفظ سجل غير قابل للحذف (FR-073 / FR-074).
/// </summary>
public interface IMemberTransferService
{
    /// <summary>
    /// ينفّذ النقل من المركز النشط (المصدر) إلى مركز هدف فعّال بصلاحية <c>Members.Transfer</c>.
    /// </summary>
    Task<MemberTransferResultDto> TransferLocalAsync(
        Guid memberId,
        TransferMemberRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ينفّذ النقل مركزياً لأي ملف بصلاحية <c>Members.Transfer</c> دون اشتراط مركز نشط محلي.
    /// </summary>
    Task<MemberTransferResultDto> TransferCentralAsync(
        Guid memberId,
        TransferMemberRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>يعيد تاريخ النقل للمنتسب ضمن المركز النشط (صلاحية العرض المحلي).</summary>
    Task<MemberTransferHistoryDto> GetHistoryLocalAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعيد تاريخ النقل مركزياً مع تسجيل الاطلاع العابر عند الحاجة.</summary>
    Task<MemberTransferHistoryDto> GetHistoryCentralAsync(
        Guid memberId,
        string accessReason,
        CancellationToken cancellationToken = default);
}
