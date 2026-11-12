using System.Globalization;

namespace API.Validation;

/// <summary>
/// يحوّل رسائل DataAnnotations وربط JSON الافتراضية إلى عربية آمنة دون تغيير شكل الاستجابة.
/// الرسائل العربية المكتوبة في الخصائص تبقى كما هي. لا يجمع أخطاء محوّل JSON الزائدة.
/// </summary>
public static class ValidationErrorText
{
    /// <summary>غلاف خطأ التحقق. الرمز يبقى VALIDATION_ERROR.</summary>
    public const string EnvelopeMessage = "بيانات الطلب غير صالحة.";

    /// <summary>يعيد رسالة عربية للحقل، أو الرسالة الأصلية إذا كانت عربية مسبقاً.</summary>
    public static string Localize(string key, string? message)
    {
        if (ContainsArabic(message))
            return message!.Trim();

        var label = Label(key);
        if (string.IsNullOrWhiteSpace(message))
            return $"قيمة الحقل {label} غير صالحة.";

        var text = message.Trim();
        if (text.Contains("e-mail", StringComparison.OrdinalIgnoreCase)
            || text.Contains("email", StringComparison.OrdinalIgnoreCase))
            return $"قيمة الحقل {label} ليست بريداً إلكترونياً صالحاً.";

        if (text.Contains("required", StringComparison.OrdinalIgnoreCase))
            return $"الحقل {label} مطلوب.";

        if (text.Contains("must be between", StringComparison.OrdinalIgnoreCase))
            return $"قيمة الحقل {label} خارج المدى المسموح.";

        if (text.Contains("minimum length", StringComparison.OrdinalIgnoreCase)
            || text.Contains("maximum length", StringComparison.OrdinalIgnoreCase))
            return $"طول الحقل {label} خارج المدى المسموح.";

        return $"قيمة الحقل {label} غير صالحة.";
    }

    private static readonly Dictionary<string, string> FieldLabels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["userName"] = "اسم المستخدم",
            ["passWord"] = "كلمة المرور",
            ["password"] = "كلمة المرور",
            ["refreshToken"] = "رمز التحديث",
        };

    private static string Label(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key is "$" or "request")
            return "الطلب";

        if (key.StartsWith("$.", StringComparison.Ordinal))
            key = key[2..];

        var leaf = key.Contains('.') ? key[(key.LastIndexOf('.') + 1)..] : key;
        return FieldLabels.TryGetValue(leaf, out var arabic) ? arabic : leaf;
    }

    private static bool ContainsArabic(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        foreach (var ch in message)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.OtherLetter && ch >= '\u0600' && ch <= '\u06FF')
                return true;
        }

        return false;
    }
}
