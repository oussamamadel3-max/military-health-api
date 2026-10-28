using Application.Dtos.ClinicWorkspace;
using Application.Exceptions;

namespace Application.Services.ClinicWorkspace;

/// <summary>مساعدات مشتركة لقوائم فحوصات العيادات (شريحة قائمة FR-039).</summary>
internal static class ClinicExamListSupport
{
    /// <summary>
    /// يطبّق باركود كرقم ملف عند الحاجة، ويعيد حجم الصفحة المطبّع ورقم الصفحة.
    /// </summary>
    public static (long? FileNumber, int PageNumber, int PageSize) NormalizeListQuery(
        ClinicExamListQuery query,
        Func<string, Exception> invalidData)
    {
        if (query is null)
            throw invalidData("معاملات قائمة الفحوصات مطلوبة.");

        if (query.ClinicId == Guid.Empty)
            throw invalidData("معرّف العيادة مطلوب.");

        var fileNumber = query.FileNumber;
        if (!string.IsNullOrWhiteSpace(query.Barcode))
        {
            var raw = query.Barcode.Trim();
            if (!long.TryParse(raw, out var fromBarcode) || fromBarcode <= 0)
            {
                throw invalidData(
                    "قيمة الباركود غير صالحة أو تالفة. أدخل رقم الملف يدوياً أو امسح رمزاً صحيحاً.");
            }

            if (fileNumber.HasValue && fileNumber.Value != fromBarcode)
            {
                throw invalidData(
                    "تعارض بين رقم الملف وقيمة الباركود. استخدم معياراً واحداً للبحث.");
            }

            fileNumber = fromBarcode;
        }

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize < 1 ? 10 : Math.Min(query.PageSize, 500);
        return (fileNumber, pageNumber, pageSize);
    }
}
