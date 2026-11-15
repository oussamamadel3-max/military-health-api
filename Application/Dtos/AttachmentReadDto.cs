namespace Application.Dtos
{
    /// <summary>
    /// قراءة مرفق/أصل ملف. حقول OwnerId/OwnerType موروثة وغير معتمدة في الكتالوج؛
    /// لا تُفعَّل عليها صلاحيات حقول في المرحلة A.
    /// </summary>
    public class AttachmentReadDto
    {
        public Guid? AttachmentId { get; set; }
        public Guid? FileAssetId { get; set; }
        public Guid? CenterId { get; set; }

        /// <summary>موروث غير مستخدم في التخزين الحالي.</summary>
        public long? OwnerId { get; set; }

        /// <summary>موروث غير مستخدم في التخزين الحالي.</summary>
        public string? OwnerType { get; set; }

        public string? FileName { get; set; }
        public string? FileExtension { get; set; }
        public string? FilePath { get; set; }
        public long? FileSize { get; set; }
        public string? FileSizeFormatted { get; set; }
        public string? ContentType { get; set; }
        public string? Description { get; set; }
        public DateTime? UploadedAt { get; set; }
        public string? FileUrl { get; set; }

        public DateTime? CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class AttachmentLinkDto
    {
        public Guid Id { get; set; }

        public string FileUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }
}
