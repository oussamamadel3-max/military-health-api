using Application.Attributes;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Enums;
using System.Reflection;

namespace Application.Common.Permissions
{
    /// <summary>
    /// إنفاذ صلاحيات الحقول على DTOs المحمية بـ <see cref="FieldPermissionAttribute"/> فقط.
    /// الحقول غير المعلَّمة لا تُمس. غياب تعيين لحقل محمي = <see cref="FieldAccessType.None"/> (Default Deny).
    /// </summary>
    public class FieldPermissionHelper
    {
        private readonly IFieldPermissionService _fieldPermissionService;
        private readonly IUserRepository _userRepository;

        public FieldPermissionHelper(
            IFieldPermissionService fieldPermissionService,
            IUserRepository userRepository)
        {
            _fieldPermissionService = fieldPermissionService;
            _userRepository = userRepository;
        }

        /// <summary>تطبيق صلاحيات الحقول على DTO واحد.</summary>
        public async Task<T> ApplyFieldPermissionsAsync<T>(
            T dto,
            string userId,
            List<string>? roleIds,
            string entityName,
            IReadOnlyDictionary<string, FieldAccessType>? jwtFieldPermissions = null) where T : class
        {
            if (dto == null) return dto!;

            var mergedPermissions = await GetMergedPermissionsAsync(userId, roleIds, entityName);
            ApplyJwtOverrides(mergedPermissions, jwtFieldPermissions, entityName);
            return ApplyPermissionsToDto(dto, mergedPermissions);
        }

        /// <summary>تطبيق صلاحيات الحقول على قائمة DTOs.</summary>
        public async Task<List<T>> ApplyFieldPermissionsAsync<T>(
            List<T> dtos,
            string userId,
            List<string>? roleIds,
            string entityName,
            IReadOnlyDictionary<string, FieldAccessType>? jwtFieldPermissions = null) where T : class
        {
            if (dtos == null || !dtos.Any()) return dtos ?? new List<T>();

            var mergedPermissions = await GetMergedPermissionsAsync(userId, roleIds, entityName);
            ApplyJwtOverrides(mergedPermissions, jwtFieldPermissions, entityName);

            var result = new List<T>(dtos.Count);
            foreach (var dto in dtos)
                result.Add(ApplyPermissionsToDto(dto, mergedPermissions));

            return result;
        }

        /// <summary>التحقق من صلاحية الكتابة على حقل محمي.</summary>
        public async Task<bool> CanWriteFieldAsync(
            string userId,
            List<string>? roleIds,
            string entityName,
            string fieldName)
        {
            var mergedPermissions = await GetMergedPermissionsAsync(userId, roleIds, entityName);
            var normalizedFieldName = NormalizeFieldKey(fieldName);
            var accessType = mergedPermissions.TryGetValue(normalizedFieldName, out var access)
                ? access
                : FieldAccessSemantics.DefaultWhenUnset;

            return FieldAccessSemantics.CanWrite(accessType);
        }

        private async Task<Dictionary<string, FieldAccessType>> GetMergedPermissionsAsync(
            string userId,
            List<string>? roleIds,
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
                if (userResult != null)
                    userPermissions = userResult.Items.ToList();
            }

            var resolvedRoleIds = await ResolveRoleIdsAsync(userId, roleIds);
            var rolePermissions = new List<FieldPermissionDto>();
            foreach (var roleId in resolvedRoleIds)
            {
                var filter = new FieldPermissionFilterDto
                {
                    RoleId = roleId,
                    EntityName = entityName
                };
                var roleResult = await _fieldPermissionService.GetFieldPermissionsAsync(1, 10000, filter);
                if (roleResult != null)
                    rolePermissions.AddRange(roleResult.Items);
            }

            return MergePermissions(userPermissions, rolePermissions);
        }

        /// <summary>
        /// يفضّل معرّفات الأدوار الفعلية من قاعدة البيانات للمستخدم.
        /// إن مُرّرت قيم تشبه GUID تُستخدم؛ أسماء الأدوار من JWT وحدها لا تُعامل كـ RoleId.
        /// </summary>
        private async Task<List<string>> ResolveRoleIdsAsync(string userId, List<string>? roleIds)
        {
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var fromDb = await _userRepository.GetActiveRoleIdsAsync(userId);
                if (fromDb.Count > 0)
                    return fromDb;
            }

            return (roleIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id) && LooksLikeRoleId(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool LooksLikeRoleId(string value)
            => Guid.TryParse(value, out _) || value.Length >= 20;

        private static Dictionary<string, FieldAccessType> MergePermissions(
            List<FieldPermissionDto> userPermissions,
            List<FieldPermissionDto> rolePermissions)
        {
            var byField = new Dictionary<string, List<FieldAccessType>>(StringComparer.OrdinalIgnoreCase);

            foreach (var perm in rolePermissions)
            {
                var key = NormalizeFieldKey(perm.FieldName);
                if (string.IsNullOrEmpty(key)) continue;
                if (!byField.TryGetValue(key, out var list))
                {
                    list = new List<FieldAccessType>();
                    byField[key] = list;
                }
                list.Add(perm.AccessType);
            }

            var userByField = new Dictionary<string, FieldAccessType>(StringComparer.OrdinalIgnoreCase);
            foreach (var perm in userPermissions)
            {
                var key = NormalizeFieldKey(perm.FieldName);
                if (string.IsNullOrEmpty(key)) continue;
                userByField[key] = perm.AccessType;
            }

            var allKeys = byField.Keys.Union(userByField.Keys, StringComparer.OrdinalIgnoreCase);
            var merged = new Dictionary<string, FieldAccessType>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in allKeys)
            {
                userByField.TryGetValue(key, out var userAccess);
                byField.TryGetValue(key, out var roleAccess);
                merged[key] = FieldAccessSemantics.ResolveEffective(
                    userByField.ContainsKey(key) ? userAccess : null,
                    roleAccess ?? Enumerable.Empty<FieldAccessType>());
            }

            return merged;
        }

        private static T ApplyPermissionsToDto<T>(T dto, Dictionary<string, FieldAccessType> permissions) where T : class
        {
            foreach (var property in typeof(T).GetProperties())
            {
                var attribute = property.GetCustomAttribute<FieldPermissionAttribute>();
                if (attribute == null) continue;

                var fieldName = NormalizeFieldKey(attribute.FieldName);
                var accessType = permissions.TryGetValue(fieldName, out var access)
                    ? access
                    : FieldAccessSemantics.DefaultWhenUnset;

                if (!FieldAccessSemantics.CanRead(accessType) && property.CanWrite)
                    property.SetValue(dto, GetDefaultValue(property.PropertyType));
            }

            return dto;
        }

        private static object? GetDefaultValue(Type type)
        {
            var underlyingNullable = Nullable.GetUnderlyingType(type);
            if (!type.IsValueType || underlyingNullable != null)
                return null;

            return Activator.CreateInstance(type);
        }

        private static void ApplyJwtOverrides(
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

            return key[..separatorIndex].Equals(entityName, StringComparison.OrdinalIgnoreCase);
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
