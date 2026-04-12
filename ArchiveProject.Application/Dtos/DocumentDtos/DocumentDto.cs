using ArchiveProject.Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ArchiveProject.Application.Dtos.DocumentDtos
{
    public class DocumentDto
    {
        public long DocumentId { get; set; }

        [FieldPermission("UserId", DisplayName = "المستخدم")]
        [StringLength(200)]
        public string? UserId { get; set; }

        [FieldPermission("DocumentTypeId", DisplayName = "نوع الوثيقة")]
        public long? DocumentTypeId { get; set; }

        [FieldPermission("DocumentNumber", DisplayName = "رقم الوثيقة")]
        public long? DocumentNumber { get; set; }

        [FieldPermission("Title", DisplayName = "العنوان")]
        [StringLength(50, MinimumLength = 2)]
        public string? Title { get; set; }

        [FieldPermission("Description", DisplayName = "الوصف")]
        [StringLength(200, MinimumLength = 2)]
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

        [StringLength(1000)]
        public string FileUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }

    public class CreateDocumentDto
    {
        [FieldPermission("DocumentTypeId", Required = false, DisplayName = "نوع الوثيقة")]
        public long? DocumentTypeId { get; set; }

        [Required(ErrorMessage = "العنوان مطلوب")]
        [StringLength(50, MinimumLength = 2)]
        [FieldPermission("Title", DisplayName = "العنوان")]
        public string Title { get; set; } = string.Empty;

        [StringLength(200, MinimumLength = 2)]
        [FieldPermission("Description", Required = false, DisplayName = "الوصف")]
        public string? Description { get; set; }

        [FieldPermission("AttachmentIds", Required = false, DisplayName = "المرفقات")]
        [MaxLength(200)]
        public List<Guid> AttachmentIds { get; set; } = new();
    }

    public class UpdateDocumentDto
    {
        [FieldPermission("DocumentTypeId", DisplayName = "نوع الوثيقة")]
        [Range(1, long.MaxValue, ErrorMessage = "نوع الوثيقة غير موجود")]
        public long? DocumentTypeId { get; set; }

        [FieldPermission("Title", DisplayName = "العنوان")]
        [StringLength(50, MinimumLength = 2)]
        public string? Title { get; set; }

        [FieldPermission("Description", DisplayName = "الوصف")]
        [StringLength(200, MinimumLength = 2)]
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
        [StringLength(50, MinimumLength = 2)]
        [FieldPermission("Title", DisplayName = "عنوان الوثيقة")]
        public string Title { get; set; } = string.Empty;

        [StringLength(200, MinimumLength = 2)]
        [FieldPermission("Description", Required = false, DisplayName = "الوصف")]
        public string? Description { get; set; }

        [FieldPermission("DocumentTypeId", DisplayName = "نوع الوثيقة")]
        [Range(1, long.MaxValue, ErrorMessage = "نوع الوثيقة غير موجود")]
        public long? DocumentTypeId { get; set; }

        [FieldPermission("AttachmentIds", Required = false, DisplayName = "المرفقات")]
        [MaxLength(200)]
        public List<Guid> AttachmentIds { get; set; } = new();
    }

    public class DocumentSearchCriteria
    {
        public long? DocumentTypeId { get; set; }

        [StringLength(20)]
        public string? SortBy { get; set; }

        public bool SortDescending { get; set; } = true;

        [StringLength(50, MinimumLength = 2)]
        public string? Title { get; set; }

        [StringLength(200, MinimumLength = 2)]
        public string? Description { get; set; }

        public DateTime? CreatedFrom { get; set; }
        public DateTime? CreatedTo { get; set; }
    }
}
