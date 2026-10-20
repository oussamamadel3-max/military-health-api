namespace Domain.Enums;

/// <summary>
/// النطاق التشغيلي لتعريف الدور (FR-016): مركزي أو مقيد بمركز.
/// </summary>
public enum RoleScope
{
    /// <summary>مقيد بمركز — إسناد الدور يتطلب CenterId.</summary>
    Center = 0,

    /// <summary>مركزي — إسناد الدور بلا CenterId.</summary>
    Central = 1
}
