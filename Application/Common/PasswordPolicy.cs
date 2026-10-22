namespace Application.Common;

public static class PasswordPolicy
{
    public const string ComplexityPattern = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{8,}$";

    public const string ComplexityErrorMessage =
        "كلمة المرور يجب ألا تقل عن 8 أحرف وأن تحتوي على حرف كبير وحرف صغير ورقم ورمز خاص.";
}
