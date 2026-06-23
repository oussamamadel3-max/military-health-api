

namespace Application.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class FieldPermissionEntityAttribute : Attribute
    {
        public string? DisplayName { get; set; }

        public FieldPermissionEntityAttribute()
        {
        }

        public FieldPermissionEntityAttribute(string displayName)
        {
            DisplayName = displayName;
        }
    }

}

