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
    public ForbiddenPermissionException(string message = "ليس لديك الصلاحية المطلوبة.")
        : base(
            message: message,
            errorCode: "FORBIDDEN_ACCESS",
            status: 403)
    {
    }
}
