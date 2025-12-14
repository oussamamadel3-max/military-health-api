using ArchiveProject.Application.Attributes;
using Microsoft.AspNetCore.Http;

namespace ArchiveProject.Application.Dtos.DocumentDtos
{
    //       Document DTO
    public class DocumentDto
    {
        public long DocumentId { get; set; }

        [FieldPermission("DepartmentId", DisplayName = "القسم")]
        public long? DepartmentId { get; set; }

        [FieldPermission("UserId", DisplayName = "المستخدم")]
        public string? UserId { get; set; }

        [FieldPermission("DocumentTypeId", DisplayName = "نوع الوثيقة")]
        public long? DocumentTypeId { get; set; }

        [FieldPermission("ArchiveGroupId", DisplayName = "مجموعة الأرشفة")]
        public long? ArchiveGroupId { get; set; }

        [FieldPermission("DocumentNumber", DisplayName = "رقم الوثيقة")]
        public long? DocumentNumber { get; set; }

        [FieldPermission("Title", DisplayName = "العنوان")]
        public string? Title { get; set; }

        [FieldPermission("Description", DisplayName = "الوصف")]
        public string? Description { get; set; }
        public List<AttachmentLinkDto> Attachments { get; set; } = new();


        // ---- Audit ----
        [FieldPermission("CreatedAt", DisplayName = "تاريخ الإنشاء")]
        public DateTime? CreatedAt { get; set; }

        [FieldPermission("CreatedBy", DisplayName = "المنشئ")]
        public string? CreatedBy { get; set; }

        [FieldPermission("UpdatedAt", DisplayName = "تاريخ التحديث")]
        public DateTime? UpdatedAt { get; set; }

        [FieldPermission("UpdatedBy", DisplayName = "المحدَّث بواسطة")]
        public string? UpdatedBy { get; set; }
    }

    public class AttachmentLinkDto
    {
        public Guid Id { get; set; }
        public string FileUrl { get; set; } = string.Empty;
    }

    //      Create Document DTO
    public class CreateDocumentDto
    {
        [FieldPermission("DepartmentId", DisplayName = "القسم")]
        public long DepartmentId { get; set; }

        [FieldPermission("DocumentTypeId", Required = false, DisplayName = "نوع الوثيقة")]
        public long? DocumentTypeId { get; set; }

        [FieldPermission("ArchiveGroupId", Required = false, DisplayName = "مجموعة الأرشفة")]
        public long? ArchiveGroupId { get; set; }

        [FieldPermission("Title", DisplayName = "العنوان")]
        public string Title { get; set; } = string.Empty;

        [FieldPermission("Description", Required = false, DisplayName = "الوصف")]
        public string? Description { get; set; }

        // ✅ قائمة معرفات المرفقات الجاهزة للربط مع الوثيقة بعد الإنشاء
        [FieldPermission("AttachmentIds", Required = false, DisplayName = "المرفقات")]
        public List<Guid> AttachmentIds { get; set; } = new();
    }

    //      Update Document DTO
    public class UpdateDocumentDto
    {
        [FieldPermission("DepartmentId", DisplayName = "القسم")]
        public long? DepartmentId { get; set; }

        [FieldPermission("DocumentTypeId", DisplayName = "نوع الوثيقة")]
        public long? DocumentTypeId { get; set; }

        [FieldPermission("ArchiveGroupId", DisplayName = "مجموعة الأرشفة")]
        public long? ArchiveGroupId { get; set; }

        [FieldPermission("Title", DisplayName = "العنوان")]
        public string? Title { get; set; }

        [FieldPermission("Description", DisplayName = "الوصف")]
        public string? Description { get; set; }

        // ✅ لتحديث ربط المرفقات (إضافة/إزالة) عند التعديل
        [FieldPermission("AttachmentIds", Required = false, DisplayName = "المرفقات")]
        public List<Guid>? AttachmentIds { get; set; }


        // ✅ وضع الاستبدال الكامل: إذا true ومعها AttachmentIds ⇒ تصبح النتيجة النهائية = AttachmentIds
        public bool? ReplaceAttachments { get; set; }

        // ✅ وضع الإضافة/الحذف التدريجي
        public List<Guid>? AddAttachmentIds { get; set; }
        public List<Guid>? RemoveAttachmentIds { get; set; }
    }

    public class CreateDocumentManyDto
    {
        [FieldPermission("Title", DisplayName = "عنوان الوثيقة")]
        public string Title { get; set; } = string.Empty;

        [FieldPermission("Description", Required = false, DisplayName = "الوصف")]
        public string? Description { get; set; }

        [FieldPermission("DepartmentId", DisplayName = "القسم")]
        public long DepartmentId { get; set; }   // قد يُضبط تلقائيًا حسب الدور

        [FieldPermission("DocumentTypeId", DisplayName = "نوع الوثيقة")]
        public long? DocumentTypeId { get; set; }

        [FieldPermission("ArchiveGroupId", Required = false, DisplayName = "مجموعة الأرشفة")]
        public long? ArchiveGroupId { get; set; }

        // ✅ المعرفات الجاهزة للربط (بدون تمرير ملفات)
        [FieldPermission("AttachmentIds", Required = false, DisplayName = "المرفقات")]
        public List<Guid> AttachmentIds { get; set; } = new();



    }


    //   Document Search Criteria
    public class DocumentSearchCriteria
    {
        // Filters
        public long? DepartmentId { get; set; }
        public long? DocumentTypeId { get; set; }
        public long? ArchiveGroupId { get; set; }
        public string? SortBy { get; set; }          // title | documentnumber | createdat
        public bool SortDescending { get; set; } = true;

        // Search
        public string? Title { get; set; }
        public string? Description { get; set; }

        // Between dates    
        public DateTime? CreatedFrom { get; set; }
        public DateTime? CreatedTo { get; set; }
    }
}
