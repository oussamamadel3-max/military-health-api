using Domain.Exceptions;

namespace Application.Exceptions;

// ==========================================
// Authorization & Authentication Exceptions
// ==========================================

public class UnauthenticatedUserException : BusinessException
{
    public UnauthenticatedUserException(string message = "غير مصرح.")
        : base(
            message: message,
            errorCode: "UNAUTHORIZED_ACCESS",
            status: 401)
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

public class CsrfTokenMissingException : BusinessException
{
    public CsrfTokenMissingException(string message = "رمز CSRF مفقود.")
        : base(
            message: message,
            errorCode: "CSRF_TOKEN_MISSING",
            status: 403)
    {
    }
}

public class CsrfTokenInvalidException : BusinessException
{
    public CsrfTokenInvalidException(string message = "رمز CSRF غير صالح.")
        : base(
            message: message,
            errorCode: "CSRF_TOKEN_INVALID",
            status: 403)
    {
    }
}

public class InvalidSessionException : BusinessException
{
    public InvalidSessionException(string message = "فشل تجديد الجلسة.")
        : base(
            message: message,
            errorCode: "AUTH_SESSION_INVALID",
            status: 401)
    {
    }
}

// ==========================================
// Login Custom Exceptions
// ==========================================

public class InvalidCredentialsException : BusinessException
{
    public InvalidCredentialsException(string message = "بيانات الدخول غير صحيحة")
        : base(
            message: message,
            errorCode: "AUTH_INVALID_CREDENTIALS",
            status: 401)
    {
    }
}

public class UserAccountDisabledException : BusinessException
{
    public UserAccountDisabledException(string message = "الحساب غير مفعّل، يرجى التواصل مع الإدارة")
        : base(
            message: message,
            errorCode: "AUTH_ACCOUNT_DISABLED",
            status: 401)
    {
    }
}

public class UserAccountLockedException : BusinessException
{
    public UserAccountLockedException(string message = "تم قفل الحساب مؤقتاً بسبب كثرة المحاولات الخاطئة")
        : base(
            message: message,
            errorCode: "AUTH_ACCOUNT_LOCKED",
            status: 401)
    {
    }
}

// ==========================================
// Register / User Creation Custom Exceptions
// ==========================================

public class UsernameAlreadyExistsException : BusinessException
{
    public UsernameAlreadyExistsException(string username = "")
        : base(
            message: string.IsNullOrWhiteSpace(username) ? "اسم المستخدم موجود مسبقاً." : $"اسم المستخدم '{username}' موجود مسبقاً.",
            errorCode: "USER_USERNAME_EXISTS",
            status: 400,
            meta: new { username })
    {
    }
}

public class EmailAlreadyExistsException : BusinessException
{
    public EmailAlreadyExistsException(string email = "")
        : base(
            message: string.IsNullOrWhiteSpace(email) ? "البريد الإلكتروني موجود مسبقاً." : $"البريد الإلكتروني '{email}' موجود مسبقاً.",
            errorCode: "USER_EMAIL_EXISTS",
            status: 400,
            meta: new { email })
    {
    }
}

public class InvalidUserDataException : BusinessException
{
    public InvalidUserDataException(string message, object? meta = null)
        : base(
            message: message,
            errorCode: "USER_INVALID_DATA",
            status: 422,
            meta: meta)
    {
    }
}
