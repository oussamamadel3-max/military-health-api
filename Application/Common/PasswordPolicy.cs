using System.Text.RegularExpressions;

namespace Application.Common;

public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public const string ComplexityPattern =
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{8,128}$";

    public const string ComplexityErrorMessage =
        "كلمة المرور يجب أن تكون بين 8 و128 محرفاً وأن تحتوي على حرف كبير وحرف صغير ورقم ورمز خاص.";

    public const string ConfirmationMismatchMessage = "تأكيد كلمة المرور غير متطابق.";

    /// <summary>MH-SEC-053: safe client message when new password matches the current one.</summary>
    public const string SameAsCurrentMessage =
        "كلمة المرور الجديدة يجب أن تختلف عن كلمة المرور الحالية.";

    public static bool IsCompliant(string? password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length is >= MinLength and <= MaxLength &&
        Regex.IsMatch(password, ComplexityPattern);
}
