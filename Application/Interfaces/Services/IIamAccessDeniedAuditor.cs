using Application.Exceptions;

namespace Application.Interfaces.Services;

/// <summary>
/// يكتب AuditEvent لرفض IAM الحساس في معاملة مستقلة حتى لا يُلغى مع Rollback للطلب.
/// </summary>
public interface IIamAccessDeniedAuditor
{
    /// <summary>
    /// يسجّل مرة واحدة لكل طلب (dedup عبر <paramref name="requestItems"/>).
    /// يتجاهل الاستثناءات غير المعلَّمة بـ <see cref="ForbiddenPermissionException.AuditAccessDenial"/>.
    /// </summary>
    Task TryRecordAsync(
        ICurrentUserService currentUser,
        ICurrentCenterService? currentCenter,
        ForbiddenPermissionException exception,
        Guid correlationId,
        IDictionary<object, object?> requestItems,
        CancellationToken cancellationToken = default);
}
