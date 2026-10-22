using Application.Common.Results;
using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services;

public interface IRoleService
{
    // 📌 إنشاء دور جديد
    Task<RoleDto> CreateRoleAsync(CreateRoleDto newRole);

    // 📌 تعديل دور موجود
    Task UpdateRoleAsync(string id, UpdateRoleDto updatedRole);

    // 📌 حذف دور
    Task DeleteRoleAsync(string id);

    // 📌 جلب دور حسب المعرف
    Task<RoleDto> GetById(string id);

    // 📌 جلب قائمة الأدوار مع التفاصيل وميزة التصفية والتصفح
    Task<PaginatedList<RoleDetailsDto>> GetRolesWithDetailsAsync(int pageNumber, int pageSize, string? name = null, bool? isActive = null);

    // 📌 تعيين دور للمستخدم
    Task AssignUserToRole(string userId, List<string> roleIds);

    // 📌 مزامنة وتحديث صلاحيات الدور (إضافة وتعديل وحذف)
    Task<string> SyncPermissionsForRoleAsync(AssignPermissionsDto dto);

    // 📌 إنشاء دور جديد مع الصلاحيات الخاصة به
    Task<RoleDetailsDto> CreateRoleWithPermissionsAsync(CreateRoleWithPermissionsDto dto);

    // 📌 جلب الأدوار للقوائم المنسدلة (Dropdown)
    Task<List<DropdownRoleDto>> GetDropdownRolesAsync();

    // 📌 جلب عدد الأدوار الفعالة
    Task<string> GetActiveRolesCount();

    // 📌 تغيير حالة الدور (تفعيل/تعطيل)
    Task<RoleStatusUpdateDto> ToggleRoleStatusAsync(Guid roleId);
}
