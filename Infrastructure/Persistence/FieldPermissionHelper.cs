using Application.Attributes;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Services;
using Domain.Enums;
using System.Reflection;

namespace Infrastructure.Persistence
{
    public class FieldPermissionHelper
    {
        private readonly IFieldPermissionService _fieldPermissionService;

        public FieldPermissionHelper(IFieldPermissionService fieldPermissionService)
        {
            _fieldPermissionService = fieldPermissionService;
        }

        /// <summary>
        /// تطبيق صلاحيات الحقول على DTO واحد
        /// </summary>
        public async Task<T> ApplyFieldPermissionsAsync<T>(
            T dto,
            string userId,
            List<string> roleIds,
            string entityName,
            IReadOnlyDictionary<string, FieldAccessType>? jwtFieldPermissions = null) where T : class
        {
            if (dto == null) return dto!;

            var mergedPermissions = await GetMergedPermissionsAsync(userId, roleIds, entityName);
            ApplyJwtOverrides(mergedPermissions, jwtFieldPermissions, entityName);

            // تطبيق الصلاحيات على DTO
            return ApplyPermissionsToDto(dto, mergedPermissions);
        }

        /// <summary>
        /// تطبيق صلاحيات الحقول على قائمة DTOs
        /// </summary>
        public async Task<List<T>> ApplyFieldPermissionsAsync<T>(
            List<T> dtos,
            string userId,
            List<string> roleIds,
            string entityName,
            IReadOnlyDictionary<string, FieldAccessType>? jwtFieldPermissions = null) where T : class
        {
            if (dtos == null || !dtos.Any()) return dtos ?? new List<T>();

            var mergedPermissions = await GetMergedPermissionsAsync(userId, roleIds, entityName);
            ApplyJwtOverrides(mergedPermissions, jwtFieldPermissions, entityName);

            var result = new List<T>(dtos.Count);
            foreach (var dto in dtos)
            {
                var filtered = ApplyPermissionsToDto(dto, mergedPermissions);
                result.Add(filtered);
            }

            return result;
        }

        /// <summary>
        /// دمج صلاحيات المستخدم والأدوار (أولوية للمستخدم)
        /// </summary>
        private Dictionary<string, FieldAccessType> MergePermissions(
            List<FieldPermissionDto> userPermissions,
            List<FieldPermissionDto> rolePermissions)
        {
            var merged = new Dictionary<string, FieldAccessType>(StringComparer.OrdinalIgnoreCase);

            // إضافة صلاحيات الأدوار أولاً
            foreach (var perm in rolePermissions)
            {
                var normalizedFieldName = NormalizeFieldKey(perm.FieldName);
                if (string.IsNullOrEmpty(normalizedFieldName)) continue;

                if (!merged.ContainsKey(normalizedFieldName))
                    merged[normalizedFieldName] = perm.AccessType;
            }

            // استبدالها بصلاحيات المستخدم إن وُجدت
            foreach (var perm in userPermissions)
            {
                var normalizedFieldName = NormalizeFieldKey(perm.FieldName);
                if (string.IsNullOrEmpty(normalizedFieldName)) continue;

                merged[normalizedFieldName] = perm.AccessType;
            }

            return merged;
        }

        /// <summary>
        /// تطبيق الصلاحيات على DTO باستخدام Reflection
        /// </summary>
        private T ApplyPermissionsToDto<T>(T dto, Dictionary<string, FieldAccessType> permissions) where T : class
        {
            var type = typeof(T);
            var properties = type.GetProperties();

            foreach (var property in properties)
            {
                var attribute = property.GetCustomAttribute<FieldPermissionAttribute>();
                if (attribute == null) continue;

                var fieldName = NormalizeFieldKey(attribute.FieldName);

                // إذا لم توجد صلاحية للمستخدم أو الدور، اعطي ReadWrite افتراضيًا
                var accessType = permissions.ContainsKey(fieldName)
                    ? permissions[fieldName]
                    : FieldAccessType.ReadWrite;

                if (accessType == FieldAccessType.None || accessType == FieldAccessType.Write)
                {
                    if (property.CanWrite)
                        property.SetValue(dto, GetDefaultValue(property.PropertyType));
                }
                // Read أو ReadWrite => نترك القيمة كما هي

            }

            return dto;
        }

        /// <summary>
        /// القيمة الافتراضية لنوع معين
        /// </summary>
        private object? GetDefaultValue(Type type)
        {
            // Nullable value types أو المراجع => null
            var underlyingNullable = Nullable.GetUnderlyingType(type);
            if (!type.IsValueType || underlyingNullable != null)
                return null;

            // أنواع ValueType غير القابلة لـ null نرجع قيمتها الافتراضية لتجنب الاستثناءات
            return Activator.CreateInstance(type);
        }

        /// <summary>
        /// التحقق من صلاحية الكتابة على حقل معين
        /// </summary>
        public async Task<bool> CanWriteFieldAsync(
            string userId,
            List<string> roleIds,
            string entityName,
            string fieldName)
        {
            var userPermissions = new List<FieldPermissionDto>();
            if (!string.IsNullOrEmpty(userId))
            {
                var filter = new FieldPermissionFilterDto
                {
                    UserId = userId,
                    EntityName = entityName
                };
                var userResult = await _fieldPermissionService.GetFieldPermissionsAsync(1, 10000, filter);
                if (userResult.Succeeded && userResult.Value != null)
                    userPermissions = userResult.Value.Items.ToList();
            }

            var rolePermissions = new List<FieldPermissionDto>();
            if (roleIds != null && roleIds.Any())
            {
                foreach (var roleId in roleIds)
                {
                    var filter = new FieldPermissionFilterDto
                    {
                        RoleId = roleId,
                        EntityName = entityName
                    };
                    var roleResult = await _fieldPermissionService.GetFieldPermissionsAsync(1, 10000, filter);
                    if (roleResult.Succeeded && roleResult.Value != null)
                        rolePermissions.AddRange(roleResult.Value.Items);
                }
            }

            var mergedPermissions = MergePermissions(userPermissions, rolePermissions);

            var normalizedFieldName = NormalizeFieldKey(fieldName);
            var accessType = mergedPermissions.ContainsKey(normalizedFieldName)
                        ? mergedPermissions[normalizedFieldName]
                        : FieldAccessType.ReadWrite; // افتراضي: صلاحية كاملة

            Console.WriteLine("=== Permissions Keys ===");

            Console.WriteLine($"{fieldName} => {accessType}");

            Console.WriteLine("=== Permissions Keys ===");
            foreach (var k in mergedPermissions.Keys)
                Console.WriteLine(k);

            return accessType == FieldAccessType.Write || accessType == FieldAccessType.ReadWrite;

        }

        private async Task<Dictionary<string, FieldAccessType>> GetMergedPermissionsAsync(
     string userId,
     List<string> roleIds,
     string entityName)
        {
            var userPermissions = new List<FieldPermissionDto>();
            if (!string.IsNullOrEmpty(userId))
            {
                var filter = new FieldPermissionFilterDto
                {
                    UserId = userId,
                    EntityName = entityName
                };
                var userResult = await _fieldPermissionService.GetFieldPermissionsAsync(1, 10000, filter);
                if (userResult.Succeeded && userResult.Value != null)
                    userPermissions = userResult.Value.Items.ToList();
            }
            Console.WriteLine("=== User Permissions ===");
            foreach (var p in userPermissions)
                Console.WriteLine($"{p.FieldName} => {p.AccessType}");

            var rolePermissions = new List<FieldPermissionDto>();
            if (roleIds != null && roleIds.Any())
            {
                foreach (var roleId in roleIds)
                {
                    var filter = new FieldPermissionFilterDto
                    {
                        RoleId = roleId,
                        EntityName = entityName
                    };
                    var roleResult = await _fieldPermissionService.GetFieldPermissionsAsync(1, 10000, filter);
                    if (roleResult.Succeeded && roleResult.Value != null)
                        rolePermissions.AddRange(roleResult.Value.Items);
                }
            }
            Console.WriteLine("=== Role Permissions ===");
            foreach (var p in rolePermissions)
                Console.WriteLine($"{p.FieldName} => {p.AccessType}");

            var merged = MergePermissions(userPermissions, rolePermissions);

            // 🔹 طباعة المحتوى بعد الدمج
            Console.WriteLine("=== Merged Permissions ===");
            foreach (var kvp in merged)
            {
                Console.WriteLine($"Field: {kvp.Key}, AccessType: {kvp.Value}");
            }
            Console.WriteLine("==========================");

            return merged;
        }

        private void ApplyJwtOverrides(
            Dictionary<string, FieldAccessType> mergedPermissions,
            IReadOnlyDictionary<string, FieldAccessType>? jwtFieldPermissions,
            string entityName)
        {
            if (jwtFieldPermissions == null || jwtFieldPermissions.Count == 0)
                return;

            foreach (var kvp in jwtFieldPermissions)
            {
                if (!IsEntityMatch(kvp.Key, entityName))
                    continue;

                var normalizedFieldName = NormalizeFieldKey(kvp.Key);
                if (string.IsNullOrEmpty(normalizedFieldName))
                    continue;

                mergedPermissions[normalizedFieldName] = kvp.Value;
            }
        }

        private static bool IsEntityMatch(string key, string entityName)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(entityName))
                return false;

            var separatorIndex = key.IndexOf(':');
            if (separatorIndex < 0)
                return false;

            var entityPart = key[..separatorIndex];
            return entityPart.Equals(entityName, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFieldKey(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                return string.Empty;

            var trimmed = fieldName.Trim();
            var lastDot = trimmed.LastIndexOf('.');
            var lastColon = trimmed.LastIndexOf(':');
            var index = Math.Max(lastDot, lastColon);

            if (index >= 0 && index + 1 < trimmed.Length)
                trimmed = trimmed[(index + 1)..];

            return trimmed;
        }

    }
}

