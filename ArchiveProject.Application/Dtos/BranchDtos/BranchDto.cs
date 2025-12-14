using ArchiveProject.Application.Attributes;

namespace ArchiveProject.Application.Dtos.BranchDtos
{
    //      Branch DTO
    public class BranchDto
    {
        public long BranchId { get; set; }

        [FieldPermission("Code", DisplayName = "رمز الفرع")]
        public string? Code { get; set; }

        [FieldPermission("BranchName", DisplayName = "اسم الفرع")]
        public string? BranchName { get; set; }

        [FieldPermission("Description", DisplayName = "الوصف")]
        public string? Description { get; set; }

        // معلومات التدقيق
        [FieldPermission("CreatedAt", DisplayName = "تاريخ الإنشاء")]
        public DateTime? CreatedAt { get; set; }

        [FieldPermission("CreatedBy", DisplayName = "المنشئ")]
        public string? CreatedBy { get; set; }

        [FieldPermission("UpdatedAt", DisplayName = "تاريخ التحديث")]
        public DateTime? UpdatedAt { get; set; }

        [FieldPermission("UpdatedBy", DisplayName = "المحدث بواسطة")]
        public string? UpdatedBy { get; set; }
    }

    //   Create Branch DTO
    public class CreateBranchDto
    {
        [FieldPermission("Code", DisplayName = "رمز الفرع")]
        public string Code { get; set; } = string.Empty;

        [FieldPermission("BranchName", DisplayName = "اسم الفرع")]
        public string BranchName { get; set; } = string.Empty;

        [FieldPermission("Description", Required = false, DisplayName = "الوصف")]
        public string? Description { get; set; }
    }

    //   Update Branch DTO
    public class UpdateBranchDto
    {
        [FieldPermission("Code", DisplayName = "رمز الفرع")]
        public string? Code { get; set; }

        [FieldPermission("BranchName", DisplayName = "اسم الفرع")]
        public string? BranchName { get; set; }

        [FieldPermission("Description", DisplayName = "الوصف")]
        public string? Description { get; set; }
    }

    //   Branch Search Criteria
    public class BranchSearchCriteria
    {
        public string? Code { get; set; }
        public string? BranchName { get; set; }
        public string? Description { get; set; }

        public string? SortBy { get; set; }
        public bool SortDescending { get; set; } = false;
    }
}
