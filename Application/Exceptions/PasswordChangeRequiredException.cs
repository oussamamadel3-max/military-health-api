using Domain.Exceptions;

namespace Application.Exceptions;

public class PasswordChangeRequiredException : BusinessException
{
    public PasswordChangeRequiredException(string message = "يجب تغيير كلمة المرور قبل استخدام النظام.")
        : base(message, "PASSWORD_CHANGE_REQUIRED", 403)
    {
    }
}
