//using ArchiveProject.Application.Attributes;

//namespace ArchiveProject.Application.Dtos.DepartmentDtos
//{
//    // 
//    //       Department DTO
//    public class DepartmentDto
//    {
//        public long DepartmentId { get; set; }

//        [FieldPermission("DepartmentName", DisplayName = "اسم القسم")]
//        public string? DepartmentName { get; set; }

//        [FieldPermission("Description", DisplayName = "الوصف")]
//        public string? Description { get; set; }

//        [FieldPermission("Code", DisplayName = "رمز القسم")]
//        public string? Code { get; set; }

//        [FieldPermission("IsActive", DisplayName = "الحالة")]
//        public bool? IsActive { get; set; }

//        [FieldPermission("BranchId", DisplayName = "رمز الفرع")]
//        public long? BranchId { get; set; }

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

//    //     Create Department DTO
//    public class CreateDepartmentDto
//    {
//        [FieldPermission("DepartmentName", DisplayName = "اسم القسم")]
//        public string DepartmentName { get; set; } = string.Empty;

//        [FieldPermission("Description", Required = false, DisplayName = "الوصف")]
//        public string? Description { get; set; }

//        [FieldPermission("Code", DisplayName = "رمز القسم")]
//        public string Code { get; set; } = string.Empty;

//        [FieldPermission("IsActive", DisplayName = "الحالة")]
//        public bool IsActive { get; set; } = true;

//        [FieldPermission("BranchId", DisplayName = "الفرع")]
//        public long BranchId { get; set; }
//    }

//    //     Update Department DTO
//    public class UpdateDepartmentDto
//    {
//        [FieldPermission("DepartmentName", DisplayName = "اسم القسم")]
//        public string? DepartmentName { get; set; }

//        [FieldPermission("Description", DisplayName = "الوصف")]
//        public string? Description { get; set; }

//        [FieldPermission("Code", DisplayName = "رمز القسم")]
//        public string? Code { get; set; }

//        [FieldPermission("IsActive", DisplayName = "الحالة")]
//        public bool? IsActive { get; set; }

//        [FieldPermission("BranchId", DisplayName = "الفرع")]
//        public long? BranchId { get; set; }
//    }

//    //   Department Search Criteria
//    public class DepartmentSearchCriteria
//    {
//        public string? DepartmentName { get; set; }
//        public string? Code { get; set; }
//        public bool? IsActive { get; set; }
//        public long? BranchId { get; set; }

//        public string? SortBy { get; set; }
//        public bool SortDescending { get; set; } = false;
//    }
//}

using ArchiveProject.Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ArchiveProject.Application.Dtos.DepartmentDtos
{
    public class DepartmentDto
    {
        public long DepartmentId { get; set; }

        [FieldPermission("DepartmentName", DisplayName = "اسم القسم")]
        [StringLength(50, MinimumLength = 2)]
        public string? DepartmentName { get; set; }

        [FieldPermission("Description", DisplayName = "الوصف")]
        [StringLength(200, MinimumLength = 2)]
        public string? Description { get; set; }

        [FieldPermission("Code", DisplayName = "رمز القسم")]
        [StringLength(50, MinimumLength = 2)]
        public string? Code { get; set; }

        [FieldPermission("IsActive", DisplayName = "الحالة")]
        public bool? IsActive { get; set; }

        [FieldPermission("BranchId", DisplayName = "رمز الفرع")]
        public long? BranchId { get; set; }

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

    public class CreateDepartmentDto
    {
        [Required(ErrorMessage = "اسم القسم مطلوب")]
        [StringLength(50, MinimumLength = 2)]
        [FieldPermission("DepartmentName", DisplayName = "اسم القسم")]
        public string DepartmentName { get; set; } = string.Empty;

        [StringLength(200, MinimumLength = 2)]
        [FieldPermission("Description", Required = false, DisplayName = "الوصف")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "رمز القسم مطلوب")]
        [StringLength(50, MinimumLength = 2)]
        [FieldPermission("Code", DisplayName = "رمز القسم")]
        public string Code { get; set; } = string.Empty;

        [FieldPermission("IsActive", DisplayName = "الحالة")]
        public bool IsActive { get; set; } = true;

        // ✅ ما عاد Required لأن BranchManager رح يكون تلقائي
        [Range(1, long.MaxValue, ErrorMessage = "الفرع غير صالح")]
        [FieldPermission("BranchId", DisplayName = "الفرع")]
        public long? BranchId { get; set; }
    }

    public class UpdateDepartmentDto
    {
        [StringLength(50, MinimumLength = 2)]
        [FieldPermission("DepartmentName", DisplayName = "اسم القسم")]
        public string? DepartmentName { get; set; }

        [StringLength(200, MinimumLength = 2)]
        [FieldPermission("Description", DisplayName = "الوصف")]
        public string? Description { get; set; }

        [StringLength(50, MinimumLength = 2)]
        [FieldPermission("Code", DisplayName = "رمز القسم")]
        public string? Code { get; set; }

        [FieldPermission("IsActive", DisplayName = "الحالة")]
        public bool? IsActive { get; set; }

        [Range(1, long.MaxValue, ErrorMessage = "الفرع غير صالح")]
        [FieldPermission("BranchId", DisplayName = "الفرع")]
        public long? BranchId { get; set; }
    }

    public class DepartmentSearchCriteria
    {
        [StringLength(50, MinimumLength = 2)]
        public string? DepartmentName { get; set; }

        [StringLength(50, MinimumLength = 2)]
        public string? Code { get; set; }

        public bool? IsActive { get; set; }
        public long? BranchId { get; set; }

        [StringLength(20)]
        public string? SortBy { get; set; }

        public bool SortDescending { get; set; } = false;
    }
}

