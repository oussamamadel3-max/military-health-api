//using ArchiveProject.Application.Attributes;

//namespace ArchiveProject.Application.Dtos.BranchDtos
//{
//    //      Branch DTO
//    public class BranchDto
//    {
//        public long BranchId { get; set; }

//        [FieldPermission("Code", DisplayName = "رمز الفرع")]
//        public string? Code { get; set; }

//        [FieldPermission("BranchName", DisplayName = "اسم الفرع")]
//        public string? BranchName { get; set; }

//        [FieldPermission("Description", DisplayName = "الوصف")]
//        public string? Description { get; set; }

//        // معلومات التدقيق
//        [FieldPermission("CreatedAt", DisplayName = "تاريخ الإنشاء")]
//        public DateTime? CreatedAt { get; set; }

//        [FieldPermission("CreatedBy", DisplayName = "المنشئ")]
//        public string? CreatedBy { get; set; }

//        [FieldPermission("UpdatedAt", DisplayName = "تاريخ التحديث")]
//        public DateTime? UpdatedAt { get; set; }

//        [FieldPermission("UpdatedBy", DisplayName = "المحدث بواسطة")]
//        public string? UpdatedBy { get; set; }
//    }

//    //   Create Branch DTO
//    public class CreateBranchDto
//    {
//        [FieldPermission("Code", DisplayName = "رمز الفرع")]
//        public string Code { get; set; } = string.Empty;

//        [FieldPermission("BranchName", DisplayName = "اسم الفرع")]
//        public string BranchName { get; set; } = string.Empty;

//        [FieldPermission("Description", Required = false, DisplayName = "الوصف")]
//        public string? Description { get; set; }
//    }

//    //   Update Branch DTO
//    public class UpdateBranchDto
//    {
//        [FieldPermission("Code", DisplayName = "رمز الفرع")]
//        public string? Code { get; set; }

//        [FieldPermission("BranchName", DisplayName = "اسم الفرع")]
//        public string? BranchName { get; set; }

//        [FieldPermission("Description", DisplayName = "الوصف")]
//        public string? Description { get; set; }
//    }

//    //   Branch Search Criteria
//    public class BranchSearchCriteria
//    {
//        public string? Code { get; set; }
//        public string? BranchName { get; set; }
//        public string? Description { get; set; }

//        public string? SortBy { get; set; }
//        public bool SortDescending { get; set; } = false;
//    }
//}

using ArchiveProject.Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ArchiveProject.Application.Dtos.BranchDtos
{
    public class BranchDto
    {
        public long BranchId { get; set; }

        [FieldPermission("Code", DisplayName = "رمز الفرع")]
        [StringLength(50)]
        public string? Code { get; set; }

        [FieldPermission("BranchName", DisplayName = "اسم الفرع")]
        [StringLength(250)]
        public string? BranchName { get; set; }

        [FieldPermission("Description", DisplayName = "الوصف")]
        [StringLength(1000)]
        public string? Description { get; set; }

        // معلومات التدقيق
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

    public class CreateBranchDto
    {
        [Required(ErrorMessage = "رمز الفرع مطلوب")]
        [StringLength(50, MinimumLength = 2)]
        [FieldPermission("Code", DisplayName = "رمز الفرع")]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "اسم الفرع مطلوب")]
        [StringLength(250, MinimumLength = 2)]
        [FieldPermission("BranchName", DisplayName = "اسم الفرع")]
        public string BranchName { get; set; } = string.Empty;

        [StringLength(1000)]
        [FieldPermission("Description", Required = false, DisplayName = "الوصف")]
        public string? Description { get; set; }
    }

    public class UpdateBranchDto
    {
        [StringLength(50, MinimumLength = 2)]
        [FieldPermission("Code", DisplayName = "رمز الفرع")]
        public string? Code { get; set; }

        [StringLength(250, MinimumLength = 2)]
        [FieldPermission("BranchName", DisplayName = "اسم الفرع")]
        public string? BranchName { get; set; }

        [StringLength(1000)]
        [FieldPermission("Description", DisplayName = "الوصف")]
        public string? Description { get; set; }
    }

    public class BranchSearchCriteria
    {
        [StringLength(50)]
        public string? Code { get; set; }

        [StringLength(250)]
        public string? BranchName { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        [StringLength(30)]
        public string? SortBy { get; set; }

        public bool SortDescending { get; set; } = false;
    }
}
