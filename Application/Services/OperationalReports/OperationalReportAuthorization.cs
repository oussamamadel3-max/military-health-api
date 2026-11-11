using Application.Common.Permissions;
using Application.Exceptions;
using Application.Interfaces.Services;

namespace Application.Services.OperationalReports;

/// <summary>
/// FR-062…068 A2: تقارير محلية بـ <see cref="MilitaryHealthPermissions.Reports.Local"/>؛
/// التفاصيل الشخصية والطبية تتطلب أيضاً <see cref="MilitaryHealthPermissions.Members.View"/>.
/// </summary>
internal static class OperationalReportAuthorization
{
    public static void EnsureMemberDetailView(IPermissionGate gate)
    {
        if (gate.Has(MilitaryHealthPermissions.Members.View))
            return;

        throw new ForbiddenPermissionException(
            "ليس لديك صلاحية عرض التفاصيل الشخصية والطبية ضمن التقارير.");
    }
}
