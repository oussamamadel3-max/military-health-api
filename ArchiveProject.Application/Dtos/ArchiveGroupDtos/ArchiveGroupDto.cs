using ArchiveProject.Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ArchiveProject.Application.Dtos.ArchiveGroupDtos
{
    public class ArchiveGroupDto
    {
        public long ArchiveGroupId { get; set; }

        [FieldPermission("Title", DisplayName = "عنوان مجموعة الأرشفة")]
        [StringLength(50, MinimumLength = 2)]
        public string? Title { get; set; }

        [FieldPermission("ArchiveGroupNumber", DisplayName = "رقم مجموعة الأرشفة")]
        public long? ArchiveGroupNumber { get; set; }

        [FieldPermission("DepartmentId", DisplayName = "القسم")]
        public long? DepartmentId { get; set; }

        [FieldPermission("UserId", DisplayName = "المستخدم المنشئ")]
        [StringLength(450)]
        public string? UserId { get; set; }

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

    public class CreateArchiveGroupDto
    {
        [Required(ErrorMessage = "عنوان مجموعة الأرشفة مطلوب")]
        [StringLength(50, MinimumLength = 2)]
        [FieldPermission("Title", DisplayName = "عنوان مجموعة الأرشفة")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "القسم مطلوب")]
        [Range(1, long.MaxValue, ErrorMessage = "القسم غير صالح")]
        [FieldPermission("DepartmentId", DisplayName = "القسم")]
        public long DepartmentId { get; set; }
    }

    public class UpdateArchiveGroupDto
    {
        [StringLength(50, MinimumLength = 2)]
        [FieldPermission("Title", DisplayName = "عنوان مجموعة الأرشفة")]
        public string? Title { get; set; }

        [Range(1, long.MaxValue, ErrorMessage = "القسم غير صالح")]
        [FieldPermission("DepartmentId", DisplayName = "القسم")]
        public long? DepartmentId { get; set; }
    }

    public class ArchiveGroupSearchCriteria
    {
        [StringLength(50, MinimumLength = 2)]
        public string? Title { get; set; }

        public long? DepartmentId { get; set; }

        public DateTime? CreatedFrom { get; set; }
        public DateTime? CreatedTo { get; set; }

        [StringLength(20)]
        public string? SortBy { get; set; }

        public bool SortDescending { get; set; }
    }
}
