using System.Net;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace Infrastructure.Persistence
{
    public static class AuditLogger
    {
        private static readonly string[] IgnoredFields =
        {
            "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy",
            "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
            "NormalizedUserName", "NormalizedEmail", "LockoutEnd",
            "AccessFailedCount", "LoginLockoutTier", "LastLoginAt",
            "Token", "TokenHash", "RefreshToken", "Password",
            "OldValues", "NewValues"
        };

        private static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static void LogChanges(
            ChangeTracker changeTracker,
            List<AuditEvent> auditEvents,
            CurrentUserDto currentUser,
            ICurrentUserService requestContext,
            Guid? centerId,
            params EntityState[] statesToLog)
        {
            var targetStates = statesToLog.Length > 0
                ? statesToLog
                : new[] { EntityState.Added, EntityState.Modified, EntityState.Deleted };

            var entries = changeTracker.Entries()
                .Where(e => e.Entity is IAuditableEntity && targetStates.Contains(e.State))
                .ToList();

            foreach (var entry in entries)
                ProcessSingleEntry(entry, auditEvents, currentUser, requestContext, centerId, entry.State);
        }

        public static void LogEntriesList(
            List<EntityEntry> entries,
            List<AuditEvent> auditEvents,
            CurrentUserDto currentUser,
            ICurrentUserService requestContext,
            Guid? centerId,
            EntityState forcedState)
        {
            foreach (var entry in entries)
                ProcessSingleEntry(entry, auditEvents, currentUser, requestContext, centerId, forcedState);
        }

        private static void ProcessSingleEntry(
            EntityEntry entry,
            List<AuditEvent> auditEvents,
            CurrentUserDto currentUser,
            ICurrentUserService requestContext,
            Guid? centerId,
            EntityState effectiveState)
        {
            if (entry.Entity is AuditEvent or CrossCenterAccess)
                return;

            string? oldValue = null;
            string? newValue = null;
            string? reason = null;

            if (effectiveState == EntityState.Added)
            {
                newValue = JsonSerializer.Serialize(ExtractAuditData(entry, isNew: true), JsonSerializerOptions);
                reason = "تم إضافة سجل جديد";
            }
            else if (effectiveState == EntityState.Modified)
            {
                var changedFields = entry.Properties
                    .Where(p => p.IsModified &&
                                !IgnoredFields.Contains(p.Metadata.Name) &&
                                !Equals(p.OriginalValue, p.CurrentValue))
                    .Select(p => p.Metadata.Name)
                    .ToList();
                if (changedFields.Count == 0)
                    return;

                oldValue = JsonSerializer.Serialize(ExtractAuditData(entry, isNew: false), JsonSerializerOptions);
                newValue = JsonSerializer.Serialize(ExtractAuditData(entry, isNew: true), JsonSerializerOptions);
                reason = "الخصائص التي جرى تعديل عليها: " + string.Join(", ", changedFields);
            }
            else if (effectiveState == EntityState.Deleted)
            {
                oldValue = JsonSerializer.Serialize(ExtractAuditData(entry, isNew: false), JsonSerializerOptions);
                reason = "تم حذف سجل";
            }

            var actorId = string.IsNullOrWhiteSpace(currentUser.UserId) || currentUser.UserId == "0"
                ? null
                : currentUser.UserId;

            var entityType = entry.Entity.GetType().Name;
            var entityId = ResolveEntityId(entry);
            auditEvents.Add(new AuditEvent
            {
                OccurredAt = DateTimeOffset.UtcNow,
                ActorUserId = actorId,
                ContextScope = centerId.HasValue ? "CENTER" : "CENTRAL",
                CenterId = centerId,
                EventType = effectiveState.ToString(),
                Result = "SUCCESS",
                EntityType = entityType,
                EntityId = entityId,
                TargetUserId = ResolveTargetUserId(entry, entityType, entityId),
                ReasonText = reason,
                OldValues = oldValue,
                NewValues = newValue,
                IPAddress = ParseIp(currentUser.IPAddress),
                UserAgent = requestContext.UserAgent,
                CorrelationId = requestContext.CorrelationId == Guid.Empty
                    ? Guid.NewGuid()
                    : requestContext.CorrelationId
            });
        }

        private static Dictionary<string, object?> ExtractAuditData(EntityEntry entry, bool isNew)
        {
            var auditData = new Dictionary<string, object?>();
            foreach (var property in entry.Properties)
            {
                if (IgnoredFields.Contains(property.Metadata.Name))
                    continue;
                if (entry.Metadata.FindNavigation(property.Metadata.Name) != null ||
                    entry.Metadata.FindSkipNavigation(property.Metadata.Name) != null)
                    continue;
                auditData[property.Metadata.Name] = isNew ? property.CurrentValue : property.OriginalValue;
            }
            return auditData;
        }

        private static string? ResolveEntityId(EntityEntry entry)
        {
            var key = entry.Metadata.FindPrimaryKey();
            if (key == null)
                return null;

            var parts = key.Properties
                .Select(p => entry.Property(p.Name).CurrentValue?.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();

            return parts.Length == 0 ? null : string.Join(":", parts);
        }

        /// <summary>
        /// يربط الحدث بحساب المستخدم المستهدف عند توفره (User أو خاصية UserId).
        /// </summary>
        private static string? ResolveTargetUserId(EntityEntry entry, string entityType, string? entityId)
        {
            if (string.Equals(entityType, nameof(Domain.Entities.Auth.User), StringComparison.Ordinal))
                return entityId;

            var userIdProp = entry.Properties.FirstOrDefault(p =>
                string.Equals(p.Metadata.Name, "UserId", StringComparison.Ordinal));
            var fromProp = userIdProp?.CurrentValue?.ToString()
                ?? userIdProp?.OriginalValue?.ToString();
            if (!string.IsNullOrWhiteSpace(fromProp))
                return fromProp;

            if (!string.IsNullOrWhiteSpace(entityId) &&
                string.Equals(entityType, nameof(Domain.Entities.Auth.UserCenter), StringComparison.Ordinal))
            {
                var colon = entityId.IndexOf(':');
                if (colon > 0)
                    return entityId[..colon];
            }

            return null;
        }

        private static IPAddress? ParseIp(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "unknown")
                return null;
            return IPAddress.TryParse(value, out var ip) ? ip : null;
        }
    }
}
