using System.Text.Json;
using Application.Interfaces.Repositories;
using Domain.Entities.Audit;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Localization;

/// <summary>
/// يحوّل حدث تدقيق مخزّن إلى تسميات عرض. لا يكتب على الحدث ولا يقرأ جداول الكيانات.
/// </summary>
public static class AuditPresentation
{
    public const int SummaryMaxLength = 180;

    private static readonly string[] NameProperties = ["NameAr", "FullName", "UserName", "Name", "Title"];

    private static readonly Dictionary<string, string> EntityLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["User"] = "المستخدم",
        ["Role"] = "الدور",
        ["Member"] = "المنتسب",
        ["Clinic"] = "العيادة",
        ["MedicalCenter"] = "المركز",
        ["AuditEvent"] = "سجل التدقيق",
        ["UserCenter"] = "إسناد مركز",
        ["UserRoleAssociation"] = "إسناد دور",
        ["UserPermission"] = "صلاحية مباشرة",
        ["UserFieldPermission"] = "صلاحية حقل",
        ["UserClinicAssignment"] = "إسناد عيادة",
        ["Consultation"] = "استشارة",
        ["Investigation"] = "استقصاء",
        ["FinalDecision"] = "القرار النهائي",
        ["MemberArchive"] = "أرشيف المنتسب",
        ["MemberTransfer"] = "نقل المنتسب",
        ["ClinicAssessment"] = "تقييم العيادة",
        ["MedicalExam"] = "فحص طبي",
        ["MedicalAttachment"] = "مرفق طبي"
    };

    private static readonly Dictionary<string, string> EventLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Added"] = "إضافة",
        ["Modified"] = "تعديل",
        ["Deleted"] = "حذف",
        ["Login"] = "تسجيل دخول",
        ["Logout"] = "تسجيل خروج",
        ["CenterChange"] = "تغيير المركز",
        ["READ"] = "اطلاع",
        ["VIEW"] = "اطلاع",
        ["MemberCreate"] = "إنشاء منتسب",
        ["MemberUpdate"] = "تعديل منتسب",
        ["MemberUpdateAfterFinalDecision"] = "تعديل منتسب بعد القرار",
        ["MemberTransferExecuted"] = "نقل المنتسب",
        ["EyeExamSaved"] = "حفظ فحص العيون",
        ["InternalExamSaved"] = "حفظ فحص الباطنة",
        ["SurgeryExamSaved"] = "حفظ فحص الجراحة",
        ["OrthopedicExamSaved"] = "حفظ فحص العظمية",
        ["EntExamSaved"] = "حفظ فحص الأذنية",
        ["ClinicAssessmentSaved"] = "حفظ تقييم العيادة",
        ["ClinicExamConflict"] = "تعارض فحص",
        ["ClinicMemberView"] = "عرض ملف المنتسب في العيادة",
        ["MemberGeneralNoteSaved"] = "حفظ ملاحظة عامة",
        ["DeferredActionCompleted"] = "إكمال إجراء مؤجل",
        ["ConsultationCreated"] = "إنشاء استشارة",
        ["ConsultationResultUpdated"] = "تحديث نتيجة استشارة",
        ["InvestigationCreated"] = "إنشاء استقصاء",
        ["InvestigationResultUpdated"] = "تحديث نتيجة استقصاء",
        ["FinalDecisionIssued"] = "إصدار القرار النهائي",
        ["FinalDecisionAmended"] = "تعديل القرار النهائي",
        ["MemberCurrentRevisionConfirmed"] = "تأكيد مراجعة المنتسب",
        ["MemberArchiveCreated"] = "إنشاء أرشيف",
        ["MemberArchiveUpdated"] = "تحديث أرشيف",
        ["ArchiveReportViewed"] = "عرض تقرير الأرشيف",
        ["ArchiveReportPrinted"] = "طباعة تقرير الأرشيف",
        ["SupervisorAssessmentFileView"] = "عرض ملف التقييم",
        ["RecruitmentExportBatchCreated"] = "إنشاء دفعة تصدير",
        ["RecruitmentExportBatchCompleted"] = "اكتمال دفعة تصدير",
        ["RecruitmentExportBatchFailed"] = "فشل دفعة تصدير",
        ["RecruitmentExportBatchDownloaded"] = "تنزيل دفعة تصدير",
        ["RecruitmentExportReexport"] = "إعادة التصدير",
        ["RecruitmentExportHistoryExported"] = "استخراج سجل التصدير",
        ["AuditLogExported"] = "استخراج سجل التدقيق",
        ["OperationalReportExport"] = "تصدير تقرير تشغيلي",
        ["OperationalReportCentralView"] = "عرض تقرير مركزي",
        ["SupervisorFollowUpExport"] = "تصدير متابعة المشرف",
        ["HistoricalCenterMigrationExecuted"] = "تنفيذ ترحيل المراكز التاريخي",
        ["HistoricalCenterMigrationVerified"] = "التحقق من ترحيل المراكز",
        ["IamAccessDenied"] = "رفض وصول",
        ["MedicalAttachmentUploaded"] = "رفع مرفق",
        ["MedicalAttachmentDownloaded"] = "تنزيل مرفق"
    };

    private static readonly Dictionary<string, string> ResultLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SUCCESS"] = "ناجح",
        ["FAILURE"] = "فشل",
        ["DENIED"] = "مرفوض"
    };

    private static readonly Dictionary<string, string> FieldLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IsActive"] = "الحالة",
        ["NameAr"] = "الاسم",
        ["FullName"] = "الاسم الكامل",
        ["UserName"] = "اسم المستخدم",
        ["Email"] = "البريد",
        ["Code"] = "الرمز",
        ["SortOrder"] = "الترتيب",
        ["CountsForFinal"] = "تحتسب في القرار",
        ["Result"] = "النتيجة",
        ["ResultStatusId"] = "نتيجة التقييم",
        ["RejectionReason"] = "سبب الرفض",
        ["ClinicalNotes"] = "الملاحظات السريرية",
        ["PhoneNumber"] = "الهاتف",
        ["MustChangePassword"] = "إلزام تغيير كلمة المرور",
        ["FileNumber"] = "رقم الملف",
        ["RecruitmentNumber"] = "رقم التجنيد"
    };

    public static AuditDisplay Present(AuditEvent auditEvent)
    {
        var newValues = ReadObject(auditEvent.NewValues);
        var oldValues = ReadObject(auditEvent.OldValues);
        var changedFields = TranslateChangedFields(auditEvent.ReasonText);
        var summary = BuildSummary(oldValues, newValues);

        return new AuditDisplay(
            LabelOrNull(EntityLabels, auditEvent.EntityType),
            LabelOrNull(EventLabels, auditEvent.EventType),
            LabelOrNull(ResultLabels, auditEvent.Result),
            ReadSnapshotName(newValues) ?? ReadSnapshotName(oldValues),
            summary,
            changedFields,
            ExportSummary(summary, changedFields, auditEvent.ReasonText));
    }

    public static async Task<IReadOnlyDictionary<Guid, string>> LoadCurrentCenterNamesAsync(
        IMedicalCenterRepository centers,
        IEnumerable<Guid?> centerIds,
        CancellationToken cancellationToken = default)
    {
        var ids = centerIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, string>();

        return await centers.Query()
            .Where(center => ids.Contains(center.CenterId))
            .Select(center => new { center.CenterId, center.NameAr })
            .ToDictionaryAsync(center => center.CenterId, center => center.NameAr, cancellationToken);
    }

    private static string? ExportSummary(string? valueSummary, string? changedFields, string? reasonText)
    {
        if (!string.IsNullOrWhiteSpace(valueSummary))
            return valueSummary;
        if (!string.IsNullOrWhiteSpace(changedFields))
            return "الحقول: " + changedFields;
        if (ContainsArabic(reasonText) && reasonText!.Contains("الخصائص التي جرى تعديل عليها:", StringComparison.Ordinal))
            return null;
        if (ContainsArabic(reasonText))
            return reasonText!.Trim();
        return null;
    }

    private static string? BuildSummary(Dictionary<string, JsonElement>? oldValues, Dictionary<string, JsonElement>? newValues)
    {
        if (oldValues is null && newValues is null)
            return null;

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (oldValues is not null)
            keys.UnionWith(oldValues.Keys);
        if (newValues is not null)
            keys.UnionWith(newValues.Keys);

        var parts = new List<string>();
        foreach (var key in keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            if (AuditSensitiveFields.IsSensitive(key) || !FieldLabels.TryGetValue(key, out var label))
                continue;

            oldValues.TryGetProperty(key, out var oldElement);
            newValues.TryGetProperty(key, out var newElement);
            if (JsonEquals(oldElement, newElement))
                continue;

            parts.Add($"{label}: {FormatValue(oldElement)} ← {FormatValue(newElement)}");
        }

        if (parts.Count == 0)
            return null;

        var text = string.Join("؛ ", parts);
        return text.Length <= SummaryMaxLength ? text : text[..(SummaryMaxLength - 1)] + "…";
    }

    private static string? TranslateChangedFields(string? reasonText)
    {
        if (string.IsNullOrWhiteSpace(reasonText))
            return null;

        const string marker = "الخصائص التي جرى تعديل عليها:";
        var index = reasonText.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var names = reasonText[(index + marker.Length)..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !AuditSensitiveFields.IsSensitive(name))
            .Select(name => FieldLabels.TryGetValue(name, out var label) ? label : null)
            .Where(label => label is not null)
            .ToArray();

        return names.Length == 0 ? null : string.Join("، ", names);
    }

    private static string? ReadSnapshotName(Dictionary<string, JsonElement>? values)
    {
        if (values is null)
            return null;

        foreach (var property in NameProperties)
        {
            if (AuditSensitiveFields.IsSensitive(property) || !values.TryGetValue(property, out var element))
                continue;

            var text = ElementToText(element);
            if (string.IsNullOrWhiteSpace(text) || Guid.TryParse(text, out _))
                continue;

            return text.Trim();
        }

        return null;
    }

    private static string FormatValue(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return "فارغ";

        var text = ElementToText(element.Value);
        if (string.IsNullOrWhiteSpace(text))
            return "فارغ";
        if (bool.TryParse(text, out var flag))
            return flag ? "نعم" : "لا";
        if (ResultLabels.TryGetValue(text, out var result))
            return result;
        if (Guid.TryParse(text, out _))
            return "—";
        if (decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))
            return text.Length <= 40 ? text : text[..39] + "…";
        if (IsUntranslatedToken(text))
            return "—";

        return text.Length <= 40 ? text : text[..39] + "…";
    }

    private static bool IsUntranslatedToken(string text)
        => !ContainsArabic(text);

    private static bool ContainsArabic(string? text)
        => !string.IsNullOrWhiteSpace(text) && text.Any(ch => ch is >= '\u0600' and <= '\u06FF');

    private static string? LabelOrNull(Dictionary<string, string> labels, string? key)
        => !string.IsNullOrWhiteSpace(key) && labels.TryGetValue(key, out var label) ? label : null;

    private static Dictionary<string, JsonElement>? ReadObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            return document.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ElementToText(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => null
        };

    private static bool JsonEquals(JsonElement? left, JsonElement? right)
    {
        if (left is null && right is null)
            return true;
        if (left is null || right is null)
            return false;
        return left.Value.GetRawText() == right.Value.GetRawText();
    }
}

public sealed record AuditDisplay(
    string? EntityLabel,
    string? EventLabel,
    string? ResultLabel,
    string? EntityDisplayName,
    string? ChangedValuesSummary,
    string? ChangedFields,
    string? ExportSummary);

internal static class AuditJsonLookup
{
    public static bool TryGetProperty(this Dictionary<string, JsonElement>? values, string key, out JsonElement? element)
    {
        element = null;
        if (values is null || !values.TryGetValue(key, out var found))
            return false;

        element = found;
        return true;
    }
}
