using System.Text;
using Application.Exceptions;

namespace Application.Common.ReferenceData;

/// <summary>
/// الأزواج الخمسة لنماذج الفحص. الرمز والترتيب والاسم العربي مصدر واحد للبذر والحارس ورسائل الفحص.
/// </summary>
public static class ExamClinicCatalog
{
    public const string EyeCode = "EYE";
    public const string EntCode = "ENT";
    public const string InternalCode = "INTERNAL";
    public const string SurgeryCode = "SURGERY";
    public const string OrthopedicCode = "ORTHOPEDIC";

    private const string ReservedNameReason = "اسم محجوز برمز غير معتمد";
    private const string SortBlockedReason = "رمز أساسي مفقود لأن الترتيب محجوز";

    private static readonly ExamClinicDefinition[] Definitions =
    [
        new(EyeCode, "العيون", 1),
        new(EntCode, "الأذنية", 2),
        new(InternalCode, "الباطنة", 3),
        new(SurgeryCode, "الجراحة", 4),
        new(OrthopedicCode, "العظمية", 5)
    ];

    private static readonly Dictionary<string, string> ReservedNames = BuildReservedNames();

    public static IReadOnlyList<ExamClinicDefinition> Clinics => Definitions;

    /// <summary>
    /// يرفض انتحال اسم أو رمز نموذج فحص، ويبقي أي زوج خارج القائمة المغلقة مسموحاً.
    /// </summary>
    public static void EnsureAssignable(string code, string nameAr)
    {
        var normalizedName = NormalizeName(nameAr);
        var reservedByName = ReservedNames.TryGetValue(normalizedName, out var expectedCode);
        var reservedByCode = Find(code);

        if (reservedByName && !CodesEqual(code, expectedCode))
        {
            throw new InvalidReferenceDataException(
                $"اسم العيادة «{nameAr.Trim()}» مرتبط بنموذج الفحص ولا يُقبل إلا مع الرمز {expectedCode}. لإنشاء عيادة إضافية اختر اسماً غير محجوز.");
        }

        if (reservedByCode is not null && !NameBelongsTo(reservedByCode.Code, normalizedName))
        {
            throw new InvalidReferenceDataException(
                $"الرمز {reservedByCode.Code} محجوز لنموذج فحص {reservedByCode.NameAr}. لإنشاء عيادة إضافية اختر رمزاً واسماً غير محجوزين.");
        }
    }

    public static string FormatExamMismatch(string? actualCode, string requiredCode)
    {
        var required = Find(requiredCode);
        var label = required?.NameAr ?? requiredCode;
        var actual = string.IsNullOrWhiteSpace(actualCode) ? "غير محدد" : actualCode.Trim();
        return $"هذه العيادة رمزها {actual}، ونموذج فحص {label} يقبل الرمز {requiredCode} فقط.";
    }

    /// <summary>
    /// يصنّف الصفوف الحالية قبل أي إدراج: الموجود بالرمز يُترك، والترتيب المشغول تعارض، والباقي يُدرج.
    /// </summary>
    public static ExamClinicClassification Classify(IEnumerable<ExamClinicSnapshot> existing)
    {
        var rows = existing.ToList();
        var inserts = new List<ExamClinicDefinition>();
        var conflicts = new List<ExamClinicConflict>();

        foreach (var definition in Definitions)
        {
            if (rows.Any(row => CodesEqual(row.Code, definition.Code)))
                continue;

            var blocker = rows.FirstOrDefault(row => row.SortOrder == definition.SortOrder);
            if (blocker is not null)
            {
                conflicts.Add(new ExamClinicConflict(
                    definition.Code,
                    definition.SortOrder,
                    blocker.ClinicId,
                    blocker.Code,
                    blocker.NameAr));
                continue;
            }

            inserts.Add(definition);
        }

        return new ExamClinicClassification(inserts, conflicts, BuildDiagnostics(rows));
    }

    public static IReadOnlyList<ExamClinicDiagnosticItem> Diagnose(IEnumerable<ExamClinicSnapshot> existing)
        => BuildDiagnostics(existing.ToList());

    /// <summary>
    /// يقص المسافات ويدمجها، ويحذف التطويل والحركات، ويوحّد أشكال الألف.
    /// </summary>
    public static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (ch is '\u0640' or '\u0670' || ch is >= '\u064B' and <= '\u065F')
                continue;

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                _ => ch
            });
        }

        return builder.ToString();
    }

    public static bool CodesEqual(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static List<ExamClinicDiagnosticItem> BuildDiagnostics(List<ExamClinicSnapshot> rows)
    {
        var diagnostics = new List<ExamClinicDiagnosticItem>();
        foreach (var row in rows)
        {
            if (ReservedNames.TryGetValue(NormalizeName(row.NameAr), out var expected)
                && !CodesEqual(row.Code, expected))
            {
                diagnostics.Add(new ExamClinicDiagnosticItem(
                    row.ClinicId, row.Code, row.NameAr, row.SortOrder, expected, ReservedNameReason));
            }
        }

        foreach (var definition in Definitions)
        {
            if (rows.Any(row => CodesEqual(row.Code, definition.Code)))
                continue;

            var blocker = rows.FirstOrDefault(row => row.SortOrder == definition.SortOrder);
            if (blocker is null)
                continue;

            if (diagnostics.Any(item => item.ClinicId == blocker.ClinicId && CodesEqual(item.ExpectedCode, definition.Code)))
                continue;

            diagnostics.Add(new ExamClinicDiagnosticItem(
                blocker.ClinicId,
                blocker.Code,
                blocker.NameAr,
                blocker.SortOrder,
                definition.Code,
                SortBlockedReason));
        }

        return diagnostics;
    }

    private static bool NameBelongsTo(string code, string normalizedName)
        => ReservedNames.TryGetValue(normalizedName, out var owner) && CodesEqual(owner, code);

    private static ExamClinicDefinition? Find(string? code)
        => Definitions.FirstOrDefault(item => CodesEqual(item.Code, code));

    private static Dictionary<string, string> BuildReservedNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in Definitions)
        {
            AddAlias(names, definition.NameAr, definition.Code);
            AddAlias(names, "عيادة " + definition.NameAr, definition.Code);
        }

        AddAlias(names, "العيادة العظمية", OrthopedicCode);
        return names;
    }

    private static void AddAlias(Dictionary<string, string> names, string name, string code)
    {
        var key = NormalizeName(name);
        if (!names.TryAdd(key, code))
            throw new InvalidOperationException($"اسم العيادة المحجوز مكرر بعد التطبيع: {key}");
    }
}

public sealed record ExamClinicDefinition(string Code, string NameAr, short SortOrder);

public sealed record ExamClinicSnapshot(Guid ClinicId, string Code, string NameAr, short SortOrder);

public sealed record ExamClinicConflict(
    string RequiredCode,
    short SortOrder,
    Guid BlockingClinicId,
    string BlockingCode,
    string BlockingName);

public sealed record ExamClinicDiagnosticItem(
    Guid ClinicId,
    string Code,
    string NameAr,
    short SortOrder,
    string ExpectedCode,
    string Reason);

public sealed record ExamClinicClassification(
    IReadOnlyList<ExamClinicDefinition> ToInsert,
    IReadOnlyList<ExamClinicConflict> Conflicts,
    IReadOnlyList<ExamClinicDiagnosticItem> Diagnostics);

public sealed record ExamClinicSeedResult(IReadOnlyList<ExamClinicDiagnosticItem> Diagnostics);
