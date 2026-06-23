
using Application.Dtos.AuthDtos;
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace Infrastructure.Persistence
{
    public static class AuditLogger
    {
        private static readonly string[] IgnoredFields = { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" };

        private static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// تجمع التغييرات من ChangeTracker حسب الحالة المطلوبة (للمرحلة الأولى).
        /// </summary>
        public static void LogChanges(ChangeTracker changeTracker, List<AuditLog> auditLogs, CurrentUserDto currentUser, params EntityState[] statesToLog)
        {
            var targetStates = statesToLog.Length > 0 ? statesToLog : new[] { EntityState.Added, EntityState.Modified, EntityState.Deleted };

            var entries = changeTracker
                .Entries()
                .Where(e => e.Entity is IAuditableEntity && targetStates.Contains(e.State))
                .ToList();

            foreach (var entry in entries)
            {
                ProcessSingleEntry(entry, auditLogs, currentUser, entry.State);
            }
        }

        /// <summary>
        /// تسجل قائمة محددة من الكيانات (للمرحلة الثانية - Added).
        /// </summary>
        public static void LogEntriesList(List<EntityEntry> entries, List<AuditLog> auditLogs, CurrentUserDto currentUser, EntityState forcedState)
        {
            foreach (var entry in entries)
            {
                ProcessSingleEntry(entry, auditLogs, currentUser, forcedState);
            }
        }

        /// <summary>
        /// منطق التسجيل الأساسي لكيان واحد.
        /// </summary>
        private static void ProcessSingleEntry(EntityEntry entry, List<AuditLog> auditLogs, CurrentUserDto currentUser, EntityState effectiveState)
        {
            long recordId = 0;

            // الحصول على الـ Primary Key
            var getPkMethod = entry.Entity.GetType().GetMethod("GetPrimaryKey");
            if (getPkMethod != null)
            {
                var result = getPkMethod.Invoke(entry.Entity, null);
                if (result is long pkValue)
                    recordId = pkValue;
            }
            else
            {
                var primaryKey = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault()?.Name ?? "Id";
                recordId = entry.Property(primaryKey).CurrentValue is long l ? l : 0;
            }

            string? oldValue = null;
            string? newValue = null;
            List<string>? changedFields = null;
            string? changeSummary = null;


            if (effectiveState == EntityState.Added)
            {
                var auditData = ExtractAuditData(entry, isNew: true);
                newValue = JsonSerializer.Serialize(auditData, JsonSerializerOptions);
                changeSummary = "تم إضافة سجل جديد";
            }
            else if (effectiveState == EntityState.Modified)
            {
                var originalData = ExtractAuditData(entry, isNew: false);
                var currentData = ExtractAuditData(entry, isNew: true);


                oldValue = JsonSerializer.Serialize(originalData, JsonSerializerOptions);
                newValue = JsonSerializer.Serialize(currentData, JsonSerializerOptions);

                changedFields = entry.Properties
                    .Where(p => p.IsModified &&
                                 !IgnoredFields.Contains(p.Metadata.Name) &&
                                 !Equals(p.OriginalValue, p.CurrentValue))
                    .Select(p => p.Metadata.Name)
                    .ToList();

                changeSummary = "الخصائص التي جرى تعديل عليها: " + string.Join(", ", changedFields);
            }
            else if (effectiveState == EntityState.Deleted)
            {
                var dbValues = entry.GetDatabaseValues();
                if (dbValues != null)
                    oldValue = JsonSerializer.Serialize(dbValues.ToObject(), JsonSerializerOptions);
                newValue = null;
                changeSummary = "تم حذف سجل";
            }

            auditLogs.Add(new AuditLog
            {
                UserId = currentUser.UserId,
                UserName = currentUser.UserName,
                IPAddress = currentUser.IPAddress,
                ActionType = effectiveState.ToString(),
                TableName = entry.Entity.GetType().Name,
                RecordId = recordId,
                ChangedFields = changedFields != null ? JsonSerializer.Serialize(changedFields) : "null",
                ChangeSummary = changeSummary,
                Timestamp = DateTimeOffset.Now,
                OldValue = oldValue,
                NewValue = newValue
            });
        }

        /// <summary>
        /// استخلاص البيانات الأساسية (Scalar Properties) فقط.
        /// </summary>
        private static Dictionary<string, object?> ExtractAuditData(EntityEntry entry, bool isNew)
        {
            var auditData = new Dictionary<string, object?>();

            foreach (var property in entry.Properties)
            {
                if (IgnoredFields.Contains(property.Metadata.Name))
                    continue;

                // تجاهل خصائص التنقل
                if (entry.Metadata.FindNavigation(property.Metadata.Name) != null || entry.Metadata.FindSkipNavigation(property.Metadata.Name) != null)
                    continue;

                auditData[property.Metadata.Name] = isNew ? property.CurrentValue : property.OriginalValue;
            }

            var primaryKey = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault()?.Name;
            if (primaryKey != null)
            {
                auditData[primaryKey] = entry.Property(primaryKey).CurrentValue;
            }

            return auditData;
        }
    }
}
