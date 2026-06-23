using Application.Attributes;

namespace Application.Dtos
{
    public class AttachmentReadDto
    {
        public Guid? AttachmentId { get; set; }

        [FieldPermission("OwnerId", DisplayName = "معرّف المالك")]
        public long? OwnerId { get; set; }

        [FieldPermission("OwnerType", DisplayName = "نوع المالك")]
        public string? OwnerType { get; set; }

        [FieldPermission("FileName", DisplayName = "اسم الملف")]
        public string? FileName { get; set; }

        [FieldPermission("FileExtension", DisplayName = "امتداد الملف")]
        public string? FileExtension { get; set; }

        [FieldPermission("FilePath", DisplayName = "مسار التخزين")]
        public string? FilePath { get; set; }

        [FieldPermission("FileSize", DisplayName = "حجم الملف")]
        public long? FileSize { get; set; }

        [FieldPermission("FileSizeFormatted", DisplayName = "حجم الملف (منسّق)")]
        public string? FileSizeFormatted { get; set; }

        [FieldPermission("ContentType", DisplayName = "نوع المحتوى")]
        public string? ContentType { get; set; }

        [FieldPermission("Description", DisplayName = "الوصف")]
        public string? Description { get; set; }

        [FieldPermission("IsProfilePhoto", DisplayName = "صورة الملف الشخصي؟")]
        public bool? IsProfilePhoto { get; set; }

        [FieldPermission("UploadedAt", DisplayName = "تاريخ الرفع")]
        public DateTime? UploadedAt { get; set; }

        [FieldPermission("FileUrl", DisplayName = "رابط التحميل")]
        public string? FileUrl { get; set; }

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


}
