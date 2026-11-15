using Domain.Enums;

namespace Application.Interfaces.Services;

/// <summary>
/// سياق المستخدم من JWT الحالي.
/// <see cref="RoleIds"/> معرّفات الأدوار (<c>role_id</c>)؛
/// <see cref="RoleNames"/> أسماء الأدوار من <c>ClaimTypes.Role</c>.
/// </summary>
public interface IUserContextService
{
    string? UserId { get; }

    /// <summary>معرّفات الأدوار الفعلية من claim <c>role_id</c>.</summary>
    IReadOnlyList<string> RoleIds { get; }

    /// <summary>أسماء الأدوار من JWT (ليست معرّفات).</summary>
    IReadOnlyList<string> RoleNames { get; }

    IReadOnlyDictionary<string, FieldAccessType> FieldPermissions { get; }
}


