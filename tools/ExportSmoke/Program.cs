using Application.Interfaces.Services;
using Application.Services;
using Application.Services.Export;
using System.Reflection;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var outDir = Path.Combine(
    @"b:\Full-Stack\Full-Stack\Full-Stack\Ministry of Defense\Base Template\backend-template",
    "export-samples");
Directory.CreateDirectory(outDir);

Console.WriteLine("Resources:");
foreach (var n in typeof(ExportService).Assembly.GetManifestResourceNames())
    Console.WriteLine("  " + n);

var svg = ExportEmbeddedResources.ReadText(ExportBrandTheme.LogoSvgFileName);
var png = ExportEmbeddedResources.ReadBytes(ExportBrandTheme.LogoPngFileName);
var font = ExportEmbeddedResources.ReadBytes(ExportBrandTheme.FontRegularFileName);
Console.WriteLine($"SVG chars={svg?.Length}, PNG bytes={png?.Length}, Font bytes={font?.Length}");

var export = new ExportService();

var arabicRows = Enumerable.Range(1, 8).Select(i => new SampleRow
{
    Name = $"مستخدم تجريبي رقم {i} — نص عربي طويل للتأكد من الالتفاف الصحيح داخل الخلية",
    Value = $"{i * 12.5m:0.##}",
    When = DateTime.Now.AddDays(-i)
}).ToList();

var multiPage = Enumerable.Range(1, 80).Select(i => new WideRow
{
    Col1 = $"بند {i}",
    Col2 = $"وصف تفصيلي طويل جداً للعنصر رقم {i} يتضمن كلمات عربية متعددة لفحص الصفحات",
    Col3 = i * 3,
    Col4 = DateTime.Today.AddDays(-i),
    Col5 = $"قيمة-{i}",
    Col6 = i % 2 == 0 ? "نشط" : "موقوف"
}).ToList();

await Save(export, arabicRows, "arabic-data", "تقرير بيانات عربية");
await Save(export, multiPage, "multi-page", "تقرير متعدد الصفحات والأعمدة");
await Save(export, Array.Empty<SampleRow>(), "empty-data", "تقرير بدون بيانات");

Console.WriteLine("Samples written to: " + outDir);

async Task Save<T>(IExportService svc, IEnumerable<T> rows, string prefix, string title)
{
    var opt = new ExportOptions
    {
        ReportName = prefix,
        SheetName = "Report",
        CustomTitle = title,
        IncludeLogo = true,
        IncludeFooter = true,
        IncludeSummary = true,
        AutoFilter = true,
        FreezeHeaders = true,
        PeriodText = "2026/01/01 → 2026/09/30",
        ColumnMappings = typeof(T) == typeof(SampleRow)
            ? new Dictionary<string, string>
            {
                ["Name"] = "البند",
                ["Value"] = "القيمة",
                ["When"] = "التاريخ"
            }
            : new Dictionary<string, string>
            {
                ["Col1"] = "البند",
                ["Col2"] = "الوصف",
                ["Col3"] = "العدد",
                ["Col4"] = "التاريخ",
                ["Col5"] = "الرمز",
                ["Col6"] = "الحالة"
            }
    };

    var pdf = await svc.ExportToPdfAsync(rows, opt);
    var xlsx = await svc.ExportToExcelAsync(rows, opt);
    await File.WriteAllBytesAsync(Path.Combine(outDir, $"{prefix}.pdf"), pdf.FileContents);
    await File.WriteAllBytesAsync(Path.Combine(outDir, $"{prefix}.xlsx"), xlsx.FileContents);
    Console.WriteLine($"Wrote {prefix}.pdf ({pdf.FileContents.Length} bytes), {prefix}.xlsx ({xlsx.FileContents.Length} bytes)");
}

sealed class SampleRow
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime When { get; set; }
}

sealed class WideRow
{
    public string Col1 { get; set; } = "";
    public string Col2 { get; set; } = "";
    public int Col3 { get; set; }
    public DateTime Col4 { get; set; }
    public string Col5 { get; set; } = "";
    public string Col6 { get; set; } = "";
}
