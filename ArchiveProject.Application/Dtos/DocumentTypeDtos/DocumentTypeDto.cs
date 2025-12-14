using ArchiveProject.Application.Attributes;

namespace ArchiveProject.Application.Dtos.DocumentTypeDtos
{
    //       DocumentType DTO
    public class DocumentTypeDto
    {
        public long DocumentTypeId { get; set; }

        [FieldPermission("DocumentTypeName", DisplayName = "اسم نوع الوثيقة")]
        public string? DocumentTypeName { get; set; }

        [FieldPermission("AllowedExtensions", DisplayName = "لاحقات الملفات المسموحة")]
        public string? AllowedExtensions { get; set; }

        // ---- Audit ----
        [FieldPermission("CreatedAt", DisplayName = "تاريخ الإنشاء")]
        public DateTime? CreatedAt { get; set; }

        [FieldPermission("CreatedBy", DisplayName = "المنشئ")]
        public string? CreatedBy { get; set; }

        [FieldPermission("UpdatedAt", DisplayName = "تاريخ التحديث")]
        public DateTime? UpdatedAt { get; set; }

        [FieldPermission("UpdatedBy", DisplayName = "المحدث بواسطة")]
        public string? UpdatedBy { get; set; }
    }

    //    Create DocumentType DTO
    public class CreateDocumentTypeDto
    {
        [FieldPermission("DocumentTypeName", DisplayName = "اسم نوع الوثيقة")]
        public string DocumentTypeName { get; set; } = string.Empty;

        [FieldPermission("AllowedExtensions", Required = false, DisplayName = "لاحقات الملفات المسموحة")]
        public string? AllowedExtensions { get; set; }
    }

    //    Update DocumentType DTO
    public class UpdateDocumentTypeDto
    {
        [FieldPermission("DocumentTypeName", DisplayName = "اسم نوع الوثيقة")]
        public string? DocumentTypeName { get; set; }

        [FieldPermission("AllowedExtensions", DisplayName = "لاحقات الملفات المسموحة")]
        public string? AllowedExtensions { get; set; }
    }

    //   DocumentType Search Criteria
    public class DocumentTypeSearchCriteria
    {
        public string? Name { get; set; }
        public string? Extensions { get; set; } // بحث نصي داخل AllowedExtensions
        public string? SortBy { get; set; }     // "name", "createdat"
        public bool SortDescending { get; set; } = false;
    }
}
