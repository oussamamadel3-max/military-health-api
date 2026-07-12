using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Application.Common.Utilities
{
    public static class EntityHelper
    {
        /// <summary>
        /// التحقق من أن الخاصية هي navigation property (علاقة مع كيان آخر)
        /// </summary>
        public static bool IsNavigationProperty(PropertyInfo property)
        {
            var propertyType = property.PropertyType;

            // استبعاد ICollection, IEnumerable, IList - عادة تكون collections للعلاقات
            if (propertyType.IsGenericType)
            {
                var genericTypeDefinition = propertyType.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(ICollection<>) ||
                    genericTypeDefinition == typeof(IEnumerable<>) ||
                    genericTypeDefinition == typeof(IList<>) ||
                    genericTypeDefinition == typeof(List<>))
                {
                    return true;
                }
            }

            // استبعاد الأنواع التي هي Entities (من Domain.Entities namespace) - navigation properties
            if (propertyType.Namespace?.StartsWith("IdentityProvider.Domain.Entities") == true)
                return true;

            // استبعاد الأنواع المعقدة (classes) التي ليست primitive types أو System types أو Application DTOs
            if (propertyType.IsClass &&
                propertyType != typeof(string) &&
                propertyType != typeof(DateTime) &&
                propertyType != typeof(DateTime?) &&
                propertyType != typeof(DateOnly) &&
                propertyType != typeof(DateOnly?) &&
                propertyType != typeof(TimeOnly) &&
                propertyType != typeof(TimeOnly?) &&
                !propertyType.IsPrimitive &&
                !propertyType.IsValueType &&
                propertyType.Namespace?.StartsWith("System") != true &&
                propertyType.Namespace?.StartsWith("IdentityProvider.Application") != true)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// التحقق من أن الخاصية هي معرف (ID) أو مفتاح رئيسي
        /// </summary>
        public static bool IsKeyProperty(PropertyInfo property)
        {
            // استبعاد الخصائص التي لديها [Key] attribute
            if (property.GetCustomAttribute<KeyAttribute>() != null)
                return true;

            // استبعاد الخصائص التي اسمها "Id" فقط (المفتاح الرئيسي)
            // ملاحظة: لا نستبعد Foreign Keys مثل "UserId" لأنها قد تكون مفيدة في بعض الحالات
            var propertyName = property.Name;
            if (propertyName.Equals("Id", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}
