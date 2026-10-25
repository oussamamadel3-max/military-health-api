using System.Globalization;
using System.Security.Cryptography;
using Application.Dtos.Members;
using Application.Exceptions;
using ClosedXML.Excel;
using Domain.Entities.Core;
using Domain.Exceptions;

namespace Application.Services.Members;

/// <summary>
/// قالب وتحقق واستخراج صفوف استيراد المنتسبين من Excel (FR-028).
/// يقبل .xlsx فقط ويقرأ قيم الخلايا دون تنفيذ صيغ.
/// </summary>
public static class MemberImportExcel
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxDataRows = 2000;

    public static readonly IReadOnlyList<string> RequiredHeaders = new[]
    {
        "الاسم الكامل",
        "اسم الأم",
        "تاريخ الميلاد",
        "الحالة الاجتماعية",
        "العمل/المهنة",
        "رقم المنتسب/التجنيد",
        "مركز التجنيد",
        "زمرة الدم",
        "سكر الدم",
        "الطول",
        "الوزن",
        "ضغط الدم",
        "النبض",
        "وشم",
        "علامات مميزة"
    };

    // أعمدة تُتجاهل إن وُجدت ولا تُستورد: رقم الملف، رقم الدور، المركز، CenterId، BMI.

    private static readonly HashSet<string> ForbiddenHeaders = new(StringComparer.Ordinal)
    {
        "نتيجة الفحص",
        "تقييم العيادة",
        "القرار النهائي",
        "نتيجة القرار",
        "مدة التأجيل",
        "سبب الرفض",
        "عيادة"
    };

    /// <summary>يبني مصنف قالب الاستيراد المعتمد برؤوس الأعمدة فقط.</summary>
    public static byte[] BuildTemplateBytes()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("قالب الاستيراد");
        for (var i = 0; i < RequiredHeaders.Count; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = RequiredHeaders[i];
            cell.Style.Font.Bold = true;
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>يتحقق من امتداد وحجم وتوقيع ملف .xlsx.</summary>
    public static void ValidateUpload(string? fileName, long length, Stream content)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new MemberImportException("اسم الملف مطلوب.");

        var name = fileName.Trim();
        if (name.Count(c => c == '.') > 1)
            throw new MemberImportException("امتداد الملف غير مسموح.");

        var ext = Path.GetExtension(name);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new MemberImportException("يُقبل ملف Excel بصيغة .xlsx فقط. نزّل القالب المعتمد.");

        if (length <= 0)
            throw new MemberImportException("الملف فارغ أو تالف.");

        if (length > MaxFileBytes)
            throw new MemberImportException($"حجم الملف يتجاوز الحد المسموح ({MaxFileBytes / (1024 * 1024)} ميجابايت).");

        if (content.CanSeek)
        {
            var pos = content.Position;
            Span<byte> magic = stackalloc byte[2];
            var read = content.Read(magic);
            content.Position = pos;
            if (read < 2 || magic[0] != (byte)'P' || magic[1] != (byte)'K')
                throw new MemberImportException("الملف تالف أو ليس بصيغة Excel المعتمدة (.xlsx).");
        }
    }

    public static string ComputeSha256Hex(Stream content)
    {
        if (content.CanSeek)
            content.Position = 0;
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(content);
        if (content.CanSeek)
            content.Position = 0;
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// يقرأ الملف ويصنّف الصفوف إلى صالح / مكرر داخل الملف / غير صالح دون الكتابة إلى قاعدة البيانات.
    /// فحص التكرار مع قاعدة البيانات يتم في طبقة الخدمة.
    /// </summary>
    public static ParsedImport ParseAndValidate(
        Stream content,
        DateOnly today,
        IReadOnlyList<ValidationRule> activeRules)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(content);
        }
        catch (Exception)
        {
            throw new MemberImportException("تعذر قراءة ملف Excel. تحقق من سلامة الملف أو نزّل القالب المعتمد.");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new MemberImportException("الملف لا يحتوي على ورقة عمل.");

            var headerMap = ReadHeaderMap(sheet);
            EnsureRequiredHeaders(headerMap);
            RejectForbiddenHeaders(headerMap);

            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
            if (lastRow <= 1)
                throw new MemberImportException("الملف لا يحتوي على سجلات للاستيراد.");

            var dataRowCount = lastRow - 1;
            if (dataRowCount > MaxDataRows)
                throw new MemberImportException($"عدد الصفوف يتجاوز الحد المسموح ({MaxDataRows}).");

            var valid = new List<MemberImportValidRow>();
            var issues = new List<MemberImportIssueRow>();
            var seenRecruitment = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
            {
                var row = sheet.Row(rowNumber);
                if (IsEmptyRow(row, headerMap))
                    continue;

                try
                {
                    var parsed = ParseRow(row, headerMap, rowNumber);
                    MemberFieldValidation.ValidateCreateOrUpdate(
                        parsed.FullName,
                        parsed.MotherName,
                        parsed.BirthDate,
                        parsed.MaritalStatusId,
                        parsed.Occupation,
                        parsed.RecruitmentNumber,
                        parsed.RecruitmentCenterId,
                        parsed.BloodTypeId,
                        parsed.BloodSugarMgDl,
                        parsed.HeightCm,
                        parsed.WeightKg,
                        parsed.BloodPressure,
                        parsed.PulseBpm,
                        today,
                        activeRules);

                    var key = parsed.RecruitmentNumber.Trim();
                    if (seenRecruitment.TryGetValue(key, out var firstRow))
                    {
                        issues.Add(new MemberImportIssueRow
                        {
                            RowNumber = rowNumber,
                            Status = "Duplicate",
                            Reason = $"تكرار داخل الملف مع الصف {firstRow}.",
                            RecruitmentNumber = key
                        });
                        continue;
                    }

                    seenRecruitment[key] = rowNumber;
                    valid.Add(parsed);
                }
                catch (BusinessException ex)
                {
                    issues.Add(new MemberImportIssueRow
                    {
                        RowNumber = rowNumber,
                        Status = "Invalid",
                        Reason = ex.Message
                    });
                }
                catch (FormatException)
                {
                    issues.Add(new MemberImportIssueRow
                    {
                        RowNumber = rowNumber,
                        Status = "Invalid",
                        Reason = "صيغة أحد الحقول غير صحيحة."
                    });
                }
            }

            return new ParsedImport(valid, issues);
        }
    }

    /// <summary>نتيجة تحليل ملف الاستيراد.</summary>
    public sealed record ParsedImport(
        IReadOnlyList<MemberImportValidRow> ValidRows,
        IReadOnlyList<MemberImportIssueRow> Issues);

    private static Dictionary<string, int> ReadHeaderMap(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var headerRow = sheet.Row(1);
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var col = 1; col <= lastCol; col++)
        {
            var raw = GetCellText(headerRow.Cell(col));
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var header = raw.Trim();
            if (map.ContainsKey(header))
                throw new MemberImportException($"تكرار عمود '{header}' في صف العناوين.");
            map[header] = col;
        }

        return map;
    }

    private static void EnsureRequiredHeaders(IReadOnlyDictionary<string, int> headerMap)
    {
        var missing = RequiredHeaders.Where(h => !headerMap.ContainsKey(h)).ToList();
        if (missing.Count > 0)
            throw new MemberImportException(
                "الملف غير مطابق للقالب المعتمد. الأعمدة الناقصة: " + string.Join("، ", missing));
    }

    private static void RejectForbiddenHeaders(IReadOnlyDictionary<string, int> headerMap)
    {
        var hit = headerMap.Keys.FirstOrDefault(k => ForbiddenHeaders.Contains(k));
        if (hit is not null)
            throw new MemberImportException(
                $"العمود '{hit}' خارج نطاق استيراد معلومات المرضى (فحوصات/قرارات غير مسموحة).");
    }

    private static bool IsEmptyRow(IXLRow row, IReadOnlyDictionary<string, int> headerMap)
    {
        foreach (var col in headerMap.Values)
        {
            if (!string.IsNullOrWhiteSpace(GetCellText(row.Cell(col))))
                return false;
        }

        return true;
    }

    private static MemberImportValidRow ParseRow(
        IXLRow row,
        IReadOnlyDictionary<string, int> headerMap,
        int rowNumber)
    {
        // A4: أعمدة رقم الملف/الدور/المركز/BMI إن وُجدت لا تُقرأ ولا تُفرض على النظام.
        var fullName = RequireText(row, headerMap, "الاسم الكامل");
        var motherName = RequireText(row, headerMap, "اسم الأم");
        var birthDate = RequireDate(row, headerMap, "تاريخ الميلاد");
        var maritalStatusId = RequireShort(row, headerMap, "الحالة الاجتماعية");
        var occupation = RequireText(row, headerMap, "العمل/المهنة");
        var recruitmentNumber = RequireText(row, headerMap, "رقم المنتسب/التجنيد");
        var recruitmentCenterId = RequireGuid(row, headerMap, "مركز التجنيد");
        var bloodTypeId = OptionalShort(row, headerMap, "زمرة الدم");
        var bloodSugar = OptionalDecimal(row, headerMap, "سكر الدم");
        var height = OptionalDecimal(row, headerMap, "الطول");
        var weight = OptionalDecimal(row, headerMap, "الوزن");
        var bloodPressure = OptionalText(row, headerMap, "ضغط الدم");
        var pulse = OptionalShort(row, headerMap, "النبض");
        var hasTattoo = OptionalBool(row, headerMap, "وشم");
        var marks = OptionalText(row, headerMap, "علامات مميزة");

        return new MemberImportValidRow
        {
            RowNumber = rowNumber,
            FullName = fullName,
            MotherName = motherName,
            BirthDate = birthDate,
            MaritalStatusId = maritalStatusId,
            Occupation = occupation,
            RecruitmentNumber = recruitmentNumber,
            RecruitmentCenterId = recruitmentCenterId,
            BloodTypeId = bloodTypeId,
            BloodSugarMgDl = bloodSugar,
            HeightCm = height,
            WeightKg = weight,
            BloodPressure = bloodPressure,
            PulseBpm = pulse,
            HasTattoo = hasTattoo,
            DistinguishingMarks = marks
        };
    }

    private static string RequireText(IXLRow row, IReadOnlyDictionary<string, int> map, string header)
    {
        var value = OptionalText(row, map, header);
        if (string.IsNullOrWhiteSpace(value))
            throw new MemberImportException($"الحقل '{header}' مطلوب.");
        return value!;
    }

    private static string? OptionalText(IXLRow row, IReadOnlyDictionary<string, int> map, string header)
    {
        if (!map.TryGetValue(header, out var col))
            return null;
        var text = GetCellText(row.Cell(col));
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static DateOnly RequireDate(IXLRow row, IReadOnlyDictionary<string, int> map, string header)
    {
        if (!map.TryGetValue(header, out var col))
            throw new MemberImportException($"الحقل '{header}' مطلوب.");

        var cell = row.Cell(col);
        if (cell.TryGetValue(out DateTime dt))
            return DateOnly.FromDateTime(dt);

        var text = GetCellText(cell);
        if (string.IsNullOrWhiteSpace(text))
            throw new MemberImportException($"الحقل '{header}' مطلوب.");

        if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            || DateOnly.TryParse(text, new CultureInfo("ar-SA"), DateTimeStyles.None, out d)
            || DateOnly.TryParseExact(text.Trim(), new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            return d;

        throw new MemberImportException($"صيغة '{header}' غير صحيحة.");
    }

    private static short RequireShort(IXLRow row, IReadOnlyDictionary<string, int> map, string header)
    {
        var value = OptionalShort(row, map, header);
        if (!value.HasValue)
            throw new MemberImportException($"الحقل '{header}' مطلوب.");
        return value.Value;
    }

    private static short? OptionalShort(IXLRow row, IReadOnlyDictionary<string, int> map, string header)
    {
        var text = OptionalText(row, map, header);
        if (text is null)
            return null;
        if (short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return v;
        throw new MemberImportException($"قيمة '{header}' يجب أن تكون رقماً صحيحاً.");
    }

    private static decimal? OptionalDecimal(IXLRow row, IReadOnlyDictionary<string, int> map, string header)
    {
        if (!map.TryGetValue(header, out var col))
            return null;
        var cell = row.Cell(col);
        if (cell.IsEmpty())
            return null;
        if (cell.TryGetValue(out double dbl))
            return Convert.ToDecimal(dbl, CultureInfo.InvariantCulture);
        var text = GetCellText(cell);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
            return d;
        throw new MemberImportException($"قيمة '{header}' يجب أن تكون رقماً.");
    }

    private static Guid RequireGuid(IXLRow row, IReadOnlyDictionary<string, int> map, string header)
    {
        var text = OptionalText(row, map, header);
        if (string.IsNullOrWhiteSpace(text))
            throw new MemberImportException($"الحقل '{header}' مطلوب.");
        if (!Guid.TryParse(text, out var id) || id == Guid.Empty)
            throw new MemberImportException($"قيمة '{header}' غير صحيحة.");
        return id;
    }

    private static bool OptionalBool(IXLRow row, IReadOnlyDictionary<string, int> map, string header)
    {
        var text = OptionalText(row, map, header);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var t = text.Trim();
        if (t is "1" or "true" or "True" or "TRUE" or "نعم" or "وشم")
            return true;
        if (t is "0" or "false" or "False" or "FALSE" or "لا")
            return false;

        throw new MemberImportException($"قيمة '{header}' يجب أن تكون نعم/لا.");
    }

    private static string GetCellText(IXLCell cell)
    {
        if (cell.IsEmpty())
            return string.Empty;

        // قراءة القيمة المخزّنة/المعروضة دون الاعتماد على تنفيذ صيغة خطرة.
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue(out DateTime dt))
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (cell.DataType == XLDataType.Number && cell.TryGetValue(out double number))
            return number.ToString(CultureInfo.InvariantCulture);

        if (cell.DataType == XLDataType.Boolean && cell.TryGetValue(out bool b))
            return b ? "true" : "false";

        return cell.GetFormattedString()?.Trim() ?? string.Empty;
    }
}
