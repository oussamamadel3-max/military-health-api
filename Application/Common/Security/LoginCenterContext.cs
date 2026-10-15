using Application.Interfaces.Services;

namespace Application.Common.Security;

public static class LoginCenterContext
{
    public const string Automatic = "Automatic";
    public const string SelectionRequired = "SelectionRequired";
    public const string None = "None";
    public const string Active = "Active";

    public const string NoCenterMessage =
        "لا يوجد مركز فعّال مسند لحسابك. لا يمكن استخدام الوظائف التشغيلية.";

    public const string NoPermissionMessage =
        "لا توجد صلاحيات تشغيلية فعّالة لحسابك. يرجى مراجعة الإدارة.";

    public const string MustChangePasswordMessage =
        "يجب تغيير كلمة المرور قبل استخدام النظام.";

    public readonly record struct Decision(string Status, AssignedCenterDto? ActiveCenter);

    public static Guid? CenterIdForNewSession(IReadOnlyList<AssignedCenterDto> centers)
        => centers.Count == 1 ? centers[0].CenterId : null;

    public static Decision Describe(IReadOnlyList<AssignedCenterDto> centers, Guid? sessionCenterId)
    {
        if (centers.Count == 0)
            return new Decision(None, null);

        if (sessionCenterId is Guid id)
        {
            var match = centers.FirstOrDefault(c => c.CenterId == id);
            if (match != null)
                return new Decision(centers.Count == 1 ? Automatic : Active, match);
        }

        return new Decision(SelectionRequired, null);
    }

    public static bool AllowsOperationalAccess(string status, int effectivePermissionCount)
        => (status == Automatic || status == Active) && effectivePermissionCount > 0;

    public static bool AllowsOperationalAccess(
        string status,
        int effectivePermissionCount,
        bool mustChangePassword)
        => !mustChangePassword && AllowsOperationalAccess(status, effectivePermissionCount);

    public static string? AccountMessage(string status, bool operationalAccess)
    {
        if (status == None)
            return NoCenterMessage;

        if ((status == Automatic || status == Active) && !operationalAccess)
            return NoPermissionMessage;

        return null;
    }

    public static string? AccountMessage(string status, bool operationalAccess, bool mustChangePassword)
    {
        if (mustChangePassword)
            return MustChangePasswordMessage;

        return AccountMessage(status, operationalAccess);
    }
}
