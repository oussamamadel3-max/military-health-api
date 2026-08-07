using Domain.Exceptions;

namespace Application.Exceptions;

// ==========================================
// User Domain Custom Exceptions
// ==========================================

public class UserNotFoundException : BusinessException
{
    public UserNotFoundException(string identifier = "")
        : base(
            message: string.IsNullOrWhiteSpace(identifier) ? "المستخدم غير موجود." : $"المستخدم '{identifier}' غير موجود.",
            errorCode: "USER_NOT_FOUND",
            status: 404,
            meta: string.IsNullOrWhiteSpace(identifier) ? null : new { identifier })
    {
    }
}

public class UsernameAlreadyExistsException : BusinessException
{
    public UsernameAlreadyExistsException(string username = "")
        : base(
            message: string.IsNullOrWhiteSpace(username) ? "اسم المستخدم موجود مسبقاً." : $"اسم المستخدم '{username}' موجود مسبقاً.",
            errorCode: "USER_USERNAME_EXISTS",
            status: 400,
            meta: string.IsNullOrWhiteSpace(username) ? null : new { username })
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
            meta: string.IsNullOrWhiteSpace(email) ? null : new { email })
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
