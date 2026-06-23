using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Identity;

public sealed class ArabicIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length)
        => new()
        {
            Code = nameof(PasswordTooShort),
            Description = $"كلمة المرور يجب ألا تقل عن {length} أحرف."
        };

    public override IdentityError PasswordRequiresNonAlphanumeric()
        => new()
        {
            Code = nameof(PasswordRequiresNonAlphanumeric),
            Description = "كلمة المرور يجب أن تحتوي على رمز خاص على الأقل."
        };

    public override IdentityError PasswordRequiresDigit()
        => new()
        {
            Code = nameof(PasswordRequiresDigit),
            Description = "كلمة المرور يجب أن تحتوي على رقم على الأقل."
        };

    public override IdentityError PasswordRequiresLower()
        => new()
        {
            Code = nameof(PasswordRequiresLower),
            Description = "كلمة المرور يجب أن تحتوي على حرف صغير على الأقل."
        };

    public override IdentityError PasswordRequiresUpper()
        => new()
        {
            Code = nameof(PasswordRequiresUpper),
            Description = "كلمة المرور يجب أن تحتوي على حرف كبير على الأقل."
        };

    public override IdentityError PasswordMismatch()
        => new()
        {
            Code = nameof(PasswordMismatch),
            Description = "كلمة المرور الحالية غير صحيحة."
        };

    public override IdentityError DuplicateUserName(string userName)
        => new()
        {
            Code = nameof(DuplicateUserName),
            Description = $"اسم المستخدم '{userName}' مستخدم مسبقاً."
        };

    public override IdentityError DuplicateEmail(string email)
        => new()
        {
            Code = nameof(DuplicateEmail),
            Description = $"البريد '{email}' مستخدم مسبقاً."
        };

    public override IdentityError InvalidEmail(string? email)
        => new()
        {
            Code = nameof(InvalidEmail),
            Description = "البريد الإلكتروني غير صالح."
        };

    public override IdentityError InvalidUserName(string? userName)
        => new()
        {
            Code = nameof(InvalidUserName),
            Description = $"اسم المستخدم غير صالح: '{userName}'."
        };

    public override IdentityError LoginAlreadyAssociated()
        => new()
        {
            Code = nameof(LoginAlreadyAssociated),
            Description = "هذا المزود مرتبط بحساب آخر."
        };

    public override IdentityError InvalidToken()
        => new()
        {
            Code = nameof(InvalidToken),
            Description = "رمز غير صالح أو منتهي."
        };
}
