using System.Net;
using System.Text.Json;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Domain.Entities.Auth;

namespace Application.Common.Security;

/// <summary>
/// مصنع أحداث إدارية على حساب مستخدم مستهدف دون تسجيل كلمات مرور أو رموز.
/// </summary>
public static class UserAccountAdminAudit
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AuditEvent Create(
        ICurrentUserService currentUser,
        string eventType,
        string targetUserId,
        string reasonText,
        object? details = null,
        Guid? centerId = null,
        string? actorUserIdOverride = null,
        bool systemActor = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUserId);

        string? actor;
        if (systemActor)
        {
            actor = null;
        }
        else if (actorUserIdOverride is not null)
        {
            actor = string.IsNullOrWhiteSpace(actorUserIdOverride) ? null : actorUserIdOverride;
        }
        else
        {
            var current = currentUser.UserId;
            actor = string.IsNullOrWhiteSpace(current) || current == "0" ? null : current;
        }

        return new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = actor,
            ContextScope = centerId.HasValue ? "CENTER" : "CENTRAL",
            CenterId = centerId,
            EventType = eventType,
            Result = "SUCCESS",
            EntityType = nameof(User),
            EntityId = targetUserId,
            TargetUserId = targetUserId,
            ReasonText = reasonText,
            NewValues = details is null ? null : JsonSerializer.Serialize(details, JsonOptions),
            UserAgent = currentUser.UserAgent,
            CorrelationId = currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(currentUser.IPAddress, out var ip) ? ip : null
        };
    }
}
