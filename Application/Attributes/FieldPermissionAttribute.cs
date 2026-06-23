namespace Application.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class FieldPermissionAttribute : Attribute
    {
        public string FieldName { get; }
        public bool Required { get; set; }
        public string? DisplayName { get; set; }

        public FieldPermissionAttribute(string fieldName, bool required = false)
        {
            FieldName = fieldName;
            Required = required;
        }
    }
}

