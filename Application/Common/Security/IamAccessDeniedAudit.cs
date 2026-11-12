using System.Net;
using System.Text.Json;
using Application.Exceptions;
using Application.Interfaces.Services;
using Domain.Entities.Audit;

namespace Application.Common.Security;

/// <summary>
/// مصنع أحداث رفض وصول IAM الحساسة (Result=DENIED) دون كلمات مرور أو رموز.
/// </summary>
public static class IamAccessDeniedAudit
{
    public const string EventType = "IamAccessDenied";
    public const string HttpItemsDedupKey = "mh.iam.access-denied.audited";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AuditEvent Create(
        ICurrentUserService currentUser,
        ForbiddenPermissionException denial,
        Guid correlationId,
        Guid? fallbackCenterId = null)
    {
        ArgumentNullException.ThrowIfNull(denial);

        var actor = string.IsNullOrWhiteSpace(currentUser.UserId) || currentUser.UserId == "0"
            ? null
            : currentUser.UserId;

        var centerId = denial.CenterId ?? fallbackCenterId;
        var details = new
        {
            operation = denial.DenialOperation,
            reason = denial.DenialReasonCode,
            permission = denial.DeniedPermission
        };

        return new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = actor,
            ContextScope = centerId.HasValue ? "CENTER" : "CENTRAL",
            CenterId = centerId,
            EventType = EventType,
            Result = "DENIED",
            EntityType = "IamAccess",
            EntityId = denial.DeniedPermission ?? denial.DenialOperation,
            TargetUserId = string.IsNullOrWhiteSpace(denial.TargetUserId) ? null : denial.TargetUserId,
            ReasonText = string.IsNullOrWhiteSpace(denial.DenialReasonCode)
                ? "رفض وصول IAM"
                : $"رفض وصول IAM: {denial.DenialReasonCode}",
            NewValues = JsonSerializer.Serialize(details, JsonOptions),
            UserAgent = currentUser.UserAgent,
            CorrelationId = correlationId == Guid.Empty
                ? (currentUser.CorrelationId == Guid.Empty ? Guid.NewGuid() : currentUser.CorrelationId)
                : correlationId,
            IPAddress = IPAddress.TryParse(currentUser.IPAddress, out var ip) ? ip : null
        };
    }
}
