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
    public string? ReasonCode { get; }
    public string? SubjectUserId { get; }

    public InvalidSessionException(
        string message = "فشل تجديد الجلسة.",
        string? reasonCode = null,
        string? subjectUserId = null)
        : base(
            message: message,
            errorCode: "AUTH_SESSION_INVALID",
            status: 401)
    {
        ReasonCode = reasonCode;
        SubjectUserId = subjectUserId;
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
    public const string ErrorCodeValue = "login_temporarily_locked";

    public const string DefaultMessage =
        "تم تجاوز عدد محاولات تسجيل الدخول. يرجى المحاولة مرة أخرى لاحقاً.";

    public int RetryAfterSeconds { get; }

    public UserAccountLockedException(int retryAfterSeconds, string? message = null)
        : base(
            message: message ?? DefaultMessage,
            errorCode: ErrorCodeValue,
            status: 429,
            meta: new { retryAfterSeconds = Math.Max(1, retryAfterSeconds) })
    {
        RetryAfterSeconds = Math.Max(1, retryAfterSeconds);
    }
}
