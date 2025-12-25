//using ArchiveProject.Application.Attributes;

//namespace ArchiveProject.Application.Dtos.DocumentTypeDtos
//{
//    //       DocumentType DTO
//    public class DocumentTypeDto
//    {
//        public long DocumentTypeId { get; set; }

//        [FieldPermission("DocumentTypeName", DisplayName = "اسم نوع الوثيقة")]
//        public string? DocumentTypeName { get; set; }

//        [FieldPermission("AllowedExtensions", DisplayName = "لاحقات الملفات المسموحة")]
//        public string? AllowedExtensions { get; set; }

//        // ---- Audit ----
//        [FieldPermission("CreatedAt", DisplayName = "تاريخ الإنشاء")]
//        public DateTime? CreatedAt { get; set; }

//        [FieldPermission("CreatedBy", DisplayName = "المنشئ")]
//        public string? CreatedBy { get; set; }

//        [FieldPermission("UpdatedAt", DisplayName = "تاريخ التحديث")]
//        public DateTime? UpdatedAt { get; set; }

//        [FieldPermission("UpdatedBy", DisplayName = "المحدث بواسطة")]
//        public string? UpdatedBy { get; set; }
//    }

//    //    Create DocumentType DTO
//    public class CreateDocumentTypeDto
//    {
//        [FieldPermission("DocumentTypeName", DisplayName = "اسم نوع الوثيقة")]
//        public string DocumentTypeName { get; set; } = string.Empty;

//        [FieldPermission("AllowedExtensions", Required = false, DisplayName = "لاحقات الملفات المسموحة")]
//        public string? AllowedExtensions { get; set; }
//    }

//    //    Update DocumentType DTO
//    public class UpdateDocumentTypeDto
//    {
//        [FieldPermission("DocumentTypeName", DisplayName = "اسم نوع الوثيقة")]
//        public string? DocumentTypeName { get; set; }

//        [FieldPermission("AllowedExtensions", DisplayName = "لاحقات الملفات المسموحة")]
//        public string? AllowedExtensions { get; set; }
//    }

//    //   DocumentType Search Criteria
//    public class DocumentTypeSearchCriteria
//    {
//        public string? Name { get; set; }
//        public string? Extensions { get; set; } // بحث نصي داخل AllowedExtensions
//        public string? SortBy { get; set; }     // "name", "createdat"
//        public bool SortDescending { get; set; } = false;
//    }
//}

using ArchiveProject.Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ArchiveProject.Application.Dtos.DocumentTypeDtos
{
    //       DocumentType DTO
    public class DocumentTypeDto
    {
        public long DocumentTypeId { get; set; }

        [FieldPermission("DocumentTypeName", DisplayName = "اسم نوع الوثيقة")]
        [StringLength(150)]
        public string? DocumentTypeName { get; set; }

        // DB: HasMaxLength(512)
        [FieldPermission("AllowedExtensions", DisplayName = "لاحقات الملفات المسموحة")]
        [StringLength(512)]
        public string? AllowedExtensions { get; set; }

        // ---- Audit ----
        [FieldPermission("CreatedAt", DisplayName = "تاريخ الإنشاء")]
        public DateTime? CreatedAt { get; set; }

        [FieldPermission("CreatedBy", DisplayName = "المنشئ")]
        [StringLength(100)]
        public string? CreatedBy { get; set; }

        [FieldPermission("UpdatedAt", DisplayName = "تاريخ التحديث")]
        public DateTime? UpdatedAt { get; set; }

        [FieldPermission("UpdatedBy", DisplayName = "المحدث بواسطة")]
        [StringLength(100)]
        public string? UpdatedBy { get; set; }
    }

    //    Create DocumentType DTO
    public class CreateDocumentTypeDto
    {
        [Required(ErrorMessage = "اسم نوع الوثيقة مطلوب")]
        [StringLength(150, MinimumLength = 2, ErrorMessage = "اسم نوع الوثيقة يجب أن يكون بين 2 و 150 حرف")]
        [FieldPermission("DocumentTypeName", DisplayName = "اسم نوع الوثيقة")]
        public string DocumentTypeName { get; set; } = string.Empty;

        // DB: HasMaxLength(512)
        [StringLength(512, ErrorMessage = "اللاحقات المسموحة يجب ألا تتجاوز 512 حرف")]
        [FieldPermission("AllowedExtensions", Required = false, DisplayName = "لاحقات الملفات المسموحة")]
        public string? AllowedExtensions { get; set; }
    }

    //    Update DocumentType DTO
    public class UpdateDocumentTypeDto
    {
        [StringLength(150, MinimumLength = 2, ErrorMessage = "اسم نوع الوثيقة يجب أن يكون بين 2 و 150 حرف")]
        [FieldPermission("DocumentTypeName", DisplayName = "اسم نوع الوثيقة")]
        public string? DocumentTypeName { get; set; }

        // DB: HasMaxLength(512)
        [StringLength(512, ErrorMessage = "اللاحقات المسموحة يجب ألا تتجاوز 512 حرف")]
        [FieldPermission("AllowedExtensions", DisplayName = "لاحقات الملفات المسموحة")]
        public string? AllowedExtensions { get; set; }
    }

    //   DocumentType Search Criteria
    public class DocumentTypeSearchCriteria
    {
        [StringLength(150)]
        public string? Name { get; set; }

        // بحث نصي داخل AllowedExtensions (مش عمود DB مباشر)
        [StringLength(200)]
        public string? Extensions { get; set; }

        // قيم مثل "name", "createdat"
        [StringLength(30)]
        public string? SortBy { get; set; }

        public bool SortDescending { get; set; } = false;
    }
}

