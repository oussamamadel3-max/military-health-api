using Domain.Exceptions;

namespace Application.Exceptions;

/// <summary>
/// تُرمى عندما تُحفظ مجموعة صلاحيات الدور بإصدار أقدم من الإصدار الحالي.
/// </summary>
public sealed class RolePermissionConcurrencyException : BusinessException
{
    public RolePermissionConcurrencyException()
        : base(
            "تغيّرت صلاحيات الدور من عملية أخرى. أعد تحميل المجموعة ثم أعد الحفظ.",
            "ROLE_PERMISSION_CONFLICT",
            409)
    {
    }
}
