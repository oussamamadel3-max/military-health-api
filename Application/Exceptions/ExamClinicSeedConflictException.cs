using System.Globalization;
using Application.Common.ReferenceData;

namespace Application.Exceptions;

/// <summary>
/// فشل تهيئة عيادات الفحص. لا يُحفظ إدراج جزئي، ولا يُعدَّل صف قائم.
/// </summary>
public sealed class ExamClinicSeedConflictException : Exception
{
    public ExamClinicSeedConflictException(string message)
        : base(message)
    {
    }

    public ExamClinicSeedConflictException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public static string Format(
        IReadOnlyList<ExamClinicConflict> conflicts,
        IReadOnlyList<ExamClinicDiagnosticItem> diagnostics)
    {
        var lines = new List<string>
        {
            "تعذر بذر عيادات الفحص. لم يُدرج أي صف ولم تُعدَّل العيادات القائمة."
        };

        foreach (var conflict in conflicts)
        {
            lines.Add(
                $"الرمز {conflict.RequiredCode} والترتيب {conflict.SortOrder.ToString(CultureInfo.InvariantCulture)} محجوزان بالعيادة {conflict.BlockingClinicId:D} الرمز {conflict.BlockingCode} الاسم {conflict.BlockingName}.");
        }

        foreach (var item in diagnostics)
        {
            lines.Add(
                $"تشخيص دون تصحيح: العيادة {item.ClinicId:D} الرمز {item.Code} الاسم {item.NameAr} الترتيب {item.SortOrder.ToString(CultureInfo.InvariantCulture)} المتوقع {item.ExpectedCode}. {item.Reason}.");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
