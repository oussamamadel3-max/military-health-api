using ArchiveProject.Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ArchiveProject.Application.Dtos.DocumentDtos
{
    public class DocumentDto
    {
        public long DocumentId { get; set; }

        [FieldPermission("DepartmentId", DisplayName = "القسم")]
        public long? DepartmentId { get; set; }

        [FieldPermission("UserId", DisplayName = "المستخدم")]
        [StringLength(450)]
        public string? UserId { get; set; }

        [FieldPermission("DocumentTypeId", DisplayName = "نوع الوثيقة")]
        public long? DocumentTypeId { get; set; }

        [FieldPermission("ArchiveGroupId", DisplayName = "مجموعة الأرشفة")]
        public long? ArchiveGroupId { get; set; }

        [FieldPermission("DocumentNumber", DisplayName = "رقم الوثيقة")]
        public long? DocumentNumber { get; set; }

        [FieldPermission("Title", DisplayName = "العنوان")]
        [StringLength(250)]
        public string? Title { get; set; }

        [FieldPermission("Description", DisplayName = "الوصف")]
        [StringLength(2000)]
        public string? Description { get; set; }

        public List<AttachmentLinkDto> Attachments { get; set; } = new();

        // ---- Audit ----
        [FieldPermission("CreatedAt", DisplayName = "تاريخ الإنشاء")]
        public DateTime? CreatedAt { get; set; }

        [FieldPermission("CreatedBy", DisplayName = "المنشئ")]
        [StringLength(100)]
        public string? CreatedBy { get; set; }

        [FieldPermission("UpdatedAt", DisplayName = "تاريخ التحديث")]
        public DateTime? UpdatedAt { get; set; }

        [FieldPermission("UpdatedBy", DisplayName = "المحدَّث بواسطة")]
        [StringLength(100)]
        public string? UpdatedBy { get; set; }
    }

    public class AttachmentLinkDto
    {
        public Guid Id { get; set; }

        [StringLength(2048)]
        public string FileUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }

    public class CreateDocumentDto
    {
        [Required(ErrorMessage = "القسم مطلوب")]
        [Range(1, long.MaxValue, ErrorMessage = "القسم غير صالح")]
        [FieldPermission("DepartmentId", DisplayName = "القسم")]
        public long DepartmentId { get; set; }

        [FieldPermission("DocumentTypeId", Required = false, DisplayName = "نوع الوثيقة")]
        public long? DocumentTypeId { get; set; }

        [FieldPermission("ArchiveGroupId", Required = false, DisplayName = "مجموعة الأرشفة")]
        public long? ArchiveGroupId { get; set; }

        [Required(ErrorMessage = "العنوان مطلوب")]
        [StringLength(250, MinimumLength = 2)]
        [FieldPermission("Title", DisplayName = "العنوان")]
        public string Title { get; set; } = string.Empty;

        [StringLength(2000)]
        [FieldPermission("Description", Required = false, DisplayName = "الوصف")]
        public string? Description { get; set; }

        [FieldPermission("AttachmentIds", Required = false, DisplayName = "المرفقات")]
        [MaxLength(200)]
        public List<Guid> AttachmentIds { get; set; } = new();
    }

    public class UpdateDocumentDto
    {
        [FieldPermission("DepartmentId", DisplayName = "القسم")]
        [Range(1, long.MaxValue, ErrorMessage = "القسم غير صالح")]
        public long? DepartmentId { get; set; }

        [FieldPermission("DocumentTypeId", DisplayName = "نوع الوثيقة")]
        [Range(1, long.MaxValue, ErrorMessage = "نوع الوثيقة غير صالح")]
        public long? DocumentTypeId { get; set; }

        [FieldPermission("ArchiveGroupId", DisplayName = "مجموعة الأرشفة")]
        [Range(1, long.MaxValue, ErrorMessage = "مجموعة الأرشفة غير صالحة")]
        public long? ArchiveGroupId { get; set; }

        [FieldPermission("Title", DisplayName = "العنوان")]
        [StringLength(250, MinimumLength = 2)]
        public string? Title { get; set; }

        [FieldPermission("Description", DisplayName = "الوصف")]
        [StringLength(2000)]
        public string? Description { get; set; }

        [FieldPermission("AttachmentIds", Required = false, DisplayName = "المرفقات")]
        [MaxLength(200)]
        public List<Guid>? AttachmentIds { get; set; }

        public bool? ReplaceAttachments { get; set; }

        [MaxLength(200)]
        public List<Guid>? AddAttachmentIds { get; set; }

        [MaxLength(200)]
        public List<Guid>? RemoveAttachmentIds { get; set; }
    }

    public class CreateDocumentManyDto
    {
        [Required(ErrorMessage = "عنوان الوثيقة مطلوب")]
        [StringLength(250, MinimumLength = 2)]
        [FieldPermission("Title", DisplayName = "عنوان الوثيقة")]
        public string Title { get; set; } = string.Empty;

        [StringLength(2000)]
        [FieldPermission("Description", Required = false, DisplayName = "الوصف")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "القسم مطلوب")]
        [Range(1, long.MaxValue, ErrorMessage = "القسم غير صالح")]
        [FieldPermission("DepartmentId", DisplayName = "القسم")]
        public long DepartmentId { get; set; }

        [FieldPermission("DocumentTypeId", DisplayName = "نوع الوثيقة")]
        [Range(1, long.MaxValue, ErrorMessage = "نوع الوثيقة غير صالح")]
        public long? DocumentTypeId { get; set; }

        [FieldPermission("ArchiveGroupId", Required = false, DisplayName = "مجموعة الأرشفة")]
        [Range(1, long.MaxValue, ErrorMessage = "مجموعة الأرشفة غير صالحة")]
        public long? ArchiveGroupId { get; set; }

        [FieldPermission("AttachmentIds", Required = false, DisplayName = "المرفقات")]
        [MaxLength(200)]
        public List<Guid> AttachmentIds { get; set; } = new();
    }

    public class DocumentSearchCriteria
    {
        public long? DepartmentId { get; set; }
        public long? DocumentTypeId { get; set; }
        public long? ArchiveGroupId { get; set; }

        [StringLength(30)]
        public string? SortBy { get; set; }

        public bool SortDescending { get; set; } = true;

        [StringLength(250)]
        public string? Title { get; set; }

        [StringLength(2000)]
        public string? Description { get; set; }

        public DateTime? CreatedFrom { get; set; }
        public DateTime? CreatedTo { get; set; }
    }
}
