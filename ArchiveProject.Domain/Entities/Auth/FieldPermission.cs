using ArchiveProject.Domain.Enums;

namespace ArchiveProject.Domain.Entities.Auth;
public class FieldPermission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string EntityName { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public FieldAccessType AccessType { get; set; } = FieldAccessType.ReadWrite;

    /// <summary>
    /// وصف اختياري لصلاحية الحقل (للاستخدام في واجهة الإدارة فقط)
    /// </summary>
    public string? Description { get; set; }

    public ICollection<UserFieldPermission> UserFieldPermissions { get; set; } = new List<UserFieldPermission>();
    public ICollection<RoleFieldPermission> RoleFieldPermissions { get; set; } = new List<RoleFieldPermission>();
}

