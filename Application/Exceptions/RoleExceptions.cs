using Domain.Exceptions;

namespace Application.Exceptions;

// ==========================================
// Role & Permission Domain Custom Exceptions
// ==========================================

public class RoleNotFoundException : BusinessException
{
    public RoleNotFoundException(string identifier = "الدور غير موجود.")
        : base(
            message: identifier,
            errorCode: "ROLE_NOT_FOUND",
            status: 404)
    {
    }
}

public class RoleAlreadyExistsException : BusinessException
{
    public RoleAlreadyExistsException(string roleName = "")
        : base(
            message: string.IsNullOrWhiteSpace(roleName) ? "الدور موجود مسبقاً." : $"الدور '{roleName}' موجود مسبقاً.",
            errorCode: "ROLE_ALREADY_EXISTS",
            status: 400)
    {
    }
}

public class InvalidRoleDataException : BusinessException
{
    public InvalidRoleDataException(string message)
        : base(
            message: message,
            errorCode: "ROLE_INVALID_DATA",
            status: 400)
    {
    }
}

public class ForbiddenPermissionException : BusinessException
{
    /// <summary>عند true يُسجَّل AuditEvent بنتيجة DENIED عبر مسار معالجة الأخطاء الموحد.</summary>
    public bool AuditAccessDenial { get; init; }

    /// <summary>رمز سبب رفض غير حسّاس (مثل permission-missing).</summary>
    public string? DenialReasonCode { get; init; }

    /// <summary>اسم العملية الداخلية للتدقيق (مثل PermissionGate.EnsureAll).</summary>
    public string? DenialOperation { get; init; }

    /// <summary>الصلاحية المطلوبة إن وُجدت.</summary>
    public string? DeniedPermission { get; init; }

    /// <summary>المستخدم المستهدف عند ارتباط الرفض بإدارة مستخدم.</summary>
    public string? TargetUserId { get; init; }

    public Guid? CenterId { get; init; }

    public ForbiddenPermissionException(string message = "ليس لديك الصلاحية المطلوبة.")
        : base(
            message: message,
            errorCode: "FORBIDDEN_ACCESS",
            status: 403)
    {
    }

    /// <summary>يرفض العملية مع تعليمها لتدقيق IAM الحساس دون تغيير عقد HTTP 403.</summary>
    public static ForbiddenPermissionException AccessDenied(
        string message,
        string operation,
        string reasonCode,
        string? permission = null,
        string? targetUserId = null,
        Guid? centerId = null)
        => new(message)
        {
            AuditAccessDenial = true,
            DenialOperation = operation,
            DenialReasonCode = reasonCode,
            DeniedPermission = permission,
            TargetUserId = targetUserId,
            CenterId = centerId
        };
}
