using ArchiveProject.Application.Attributes;
using ArchiveProject.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace ArchiveProject.Infrastructure.Persistence
{


    public static class PermissionHelperUtilities
    {
        /// <summary>
        /// التحقق من صلاحيات جميع الحقول التي أرسلها المستخدم
        /// </summary>
        public static async Task<List<string>> ValidateFieldPermissionsFromJwtAsync<T>(
            T dto,
            IReadOnlyDictionary<string, FieldAccessType> jwtFieldPermissions,
            string entityName,
            FieldPermissionHelper fieldPermissionHelper,
            string userId,
            List<string> roleIds) where T : class
        {
            var errors = new List<string>();
            var dtoProps = typeof(T).GetProperties();

            foreach (var prop in dtoProps)
            {
                var attr = prop.GetCustomAttribute<FieldPermissionAttribute>();
                if (attr == null) continue;

                var value = prop.GetValue(dto);
                var key = $"{entityName}:{attr.FieldName}";
                var isRequired = prop.GetCustomAttribute<RequiredAttribute>() != null;

                FieldAccessType? jwtAccessType = null;
                if (jwtFieldPermissions != null && jwtFieldPermissions.TryGetValue(key, out var jwtAccess))
                {
                    jwtAccessType = jwtAccess;
                }

                bool? cachedDbAccess = null;
                async Task<bool> EnsureDbAccessAsync()
                {
                    if (!cachedDbAccess.HasValue)
                    {
                        cachedDbAccess = await fieldPermissionHelper
                            .CanWriteFieldAsync(userId, roleIds, entityName, attr.FieldName);
                    }
                    return cachedDbAccess.Value;
                }

                if (HasNonDefaultValue(value, prop.PropertyType))
                {
                    if (jwtAccessType.HasValue)
                    {
                        if (!HasWritePermission(jwtAccessType.Value))
                            errors.Add($"ليس لديك صلاحية كتابة على الحقل: {attr.FieldName}");

                        continue;
                    }

                    if (!await EnsureDbAccessAsync())
                        errors.Add($"ليس لديك صلاحية كتابة على الحقل: {attr.FieldName}");

                    continue;
                }

                if (isRequired)
                {
                    bool hasWritePermission;
                    if (jwtAccessType.HasValue)
                        hasWritePermission = HasWritePermission(jwtAccessType.Value);
                    else
                        hasWritePermission = await EnsureDbAccessAsync();

                    if (!hasWritePermission)
                        errors.Add($"لا يمكنك إنشاء أو تحديث العنصر لأن الحقل الإجباري {attr.FieldName} يتطلب صلاحية كتابة.");
                }
            }

            return errors;
        }



        /// <summary>
        /// تعيين الحقول من DTO إلى Entity بناءً على الصلاحيات
        /// </summary>
        public static async Task AssignFieldsWithPermissionsAsync<TDto, TEntity>(
            TDto dto,
            TEntity entity,
            string userId,
            List<string> roleIds,
            string entityName,
            FieldPermissionHelper fieldPermissionHelper)
            where TDto : class
            where TEntity : class
        {
            var dtoProps = typeof(TDto).GetProperties();
            var entityProps = typeof(TEntity).GetProperties().ToDictionary(p => p.Name, p => p);

            foreach (var prop in dtoProps)
            {
                var attr = prop.GetCustomAttribute<FieldPermissionAttribute>();
                if (attr == null) continue;

                // التحقق من الصلاحية
                if (await fieldPermissionHelper.CanWriteFieldAsync(userId, roleIds, entityName, attr.FieldName))
                {
                    // تعيين القيمة
                    if (entityProps.TryGetValue(prop.Name, out var entityProp) && entityProp.CanWrite)
                    {
                        var value = prop.GetValue(dto);
                        entityProp.SetValue(entity, value);
                    }
                }
            }
        }

        /// <summary>
        /// التحقق من أن القيمة ليست القيمة الافتراضية
        /// </summary>
        public static bool HasNonDefaultValue(object? value, Type type)
        {
            if (value == null)
                return false;

            // للأنواع القيمية (int, decimal, bool, etc.)
            if (type.IsValueType)
            {
                var defaultValue = Activator.CreateInstance(type);
                return !value.Equals(defaultValue);
            }

            // للـ string
            if (type == typeof(string))
            {
                return !string.IsNullOrWhiteSpace(value as string);
            }

            // لباقي الأنواع المرجعية
            return true;
        }



        private static bool HasWritePermission(FieldAccessType accessType)
        {
            return accessType == FieldAccessType.Write || accessType == FieldAccessType.ReadWrite;
        }
    }
}

