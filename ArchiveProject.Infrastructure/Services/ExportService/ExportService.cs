using ArchiveProject.Application.Common.Interfaces.ExportInterfaces;
using ArchiveProject.Application.Dtos.ExportDtos;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ArchiveProject.Infrastructure.Services.Exporting
{
    public class ExportService : IExportService
    {
        private readonly Assembly _assembly;
        private readonly string? _logoResId;

        public ExportService()
        {
            _assembly = typeof(ExportService).Assembly;
            _logoResId = "ArchiveProject.Infrastructure.Resources.Images.logo-192x192.png";
        }

        public Task<ExportFileDto> QuickExportToExcelAsync<T>(IEnumerable<T> data, string reportName)
            => ExportToExcelAsync(data, new ExportOptions
            {
                ReportName = reportName,
                SheetName = reportName,
                CustomTitle = reportName
            });

        public Task<ExportFileDto> QuickExportToPdfAsync<T>(IEnumerable<T> data, string reportName)
            => ExportToPdfAsync(data, new ExportOptions
            {
                ReportName = reportName,
                CustomTitle = reportName,
                IncludeLogo = true
            });

        // =========================
        // Excel
        // =========================
        public Task<ExportFileDto> ExportToExcelAsync<T>(IEnumerable<T> data, ExportOptions options)
        {
            using var wb = new XLWorkbook();

            var sheetNameRaw = string.IsNullOrWhiteSpace(options.SheetName) ? options.ReportName : options.SheetName;
            var sheetName = EnsureUniqueSheetName(wb, MakeValidExcelSheetName(sheetNameRaw, "تقرير"));
            var fileName = MakeSafeFileName(options.ReportName, "Report");

            var ws = wb.Worksheets.Add(sheetName);
            ws.RightToLeft = true;

            // ستايل عام للورقة
            ws.Style.Font.FontName = "Segoe UI";
            ws.Style.Font.FontSize = 10;
            ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Style.Alignment.WrapText = true;

            var props = typeof(T).GetProperties().ToArray();
            if (options.ExcludedColumns?.Any() == true)
                props = props.Where(p => !options.ExcludedColumns.Contains(p.Name)).ToArray();

            var list = data?.ToList() ?? new List<T>();

            var title = options.CustomTitle ?? options.ReportName;
            CreateHeader(ws, Math.Max(1, props.Length), title, options);

            if (props.Length == 0)
            {
                ws.Cell(4, 1).Value = "لا توجد أعمدة للعرض";
                ws.Cell(4, 1).Style.Font.Bold = true;
                ws.Cell(4, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            else
            {
                var headerRow = CreateColumnHeaders(ws, props, options);
                var endRow = FillRows(ws, list, props, headerRow, options);

                // Table styling range
                if (list.Any())
                {
                    var rng = ws.Range(headerRow, 1, endRow - 1, props.Length);

                    // Borders
                    rng.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    rng.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    rng.Style.Border.OutsideBorderColor = XLColor.FromHtml("#111827");
                    rng.Style.Border.InsideBorderColor = XLColor.FromHtml("#D1D5DB");

                    // Zebra rows for data only
                    for (int r = headerRow + 1; r <= endRow - 1; r++)
                    {
                        var rowRange = ws.Range(r, 1, r, props.Length);
                        rowRange.Style.Fill.BackgroundColor =
                            (r - (headerRow + 1)) % 2 == 0 ? XLColor.FromHtml("#FFFFFF") : XLColor.FromHtml("#F9FAFB");
                    }
                }

                // Autofilter + Freeze
                if (list.Any() && options.AutoFilter)
                {
                    ws.Range(headerRow, 1, endRow - 1, props.Length).SetAutoFilter();
                    if (options.FreezeHeaders)
                        ws.SheetView.FreezeRows(headerRow);
                }

                AutoFit(ws, props.Length);

                if (options.IncludeSummary)
                    CreateSummary(ws, list.Cast<object>().ToList(), props, endRow, options);

                if (options.IncludeFooter)
                    AddFooter(ws, endRow + 7, props.Length);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);

            return Task.FromResult(new ExportFileDto
            {
                FileContents = ms.ToArray(),
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                FileName = $"{fileName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            });
        }

        // =========================
        // PDF
        // =========================
        public Task<ExportFileDto> ExportToPdfAsync<T>(IEnumerable<T> data, ExportOptions options)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            TryRegisterArabicFont();

            var props = typeof(T).GetProperties().ToArray();
            if (options.ExcludedColumns?.Any() == true)
                props = props.Where(p => !options.ExcludedColumns.Contains(p.Name)).ToArray();

            var list = data?.ToList() ?? new List<T>();

            // RTL عملي: نعكس الأعمدة
            var propsRtl = props.Reverse().ToArray();

            var titleText = CleanForXml(options.CustomTitle ?? options.ReportName);
            var metaLine = BuildMetaLine(options);
            var printDate = DateTime.Now.ToString(options.DateFormat ?? "yyyy/MM/dd");

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(22);

                    // خط عربي + ستايل عام
                    page.DefaultTextStyle(x => x
                        .FontFamily("Noto Naskh Arabic")
                        .FontSize(11)
                        .FontColor(Colors.Grey.Darken4));

                    // ===== Header =====
                    page.Header().PaddingBottom(10).Column(h =>
                    {
                        h.Item().Row(row =>
                        {
                            // ✅ لوغو يسار
                            if (options.IncludeLogo)
                                TryDrawLogo(row);

                            // ✅ عنوان وسط
                            row.RelativeItem().AlignCenter().Text(titleText)
                               .FontSize(18).SemiBold().FontColor(Colors.Blue.Darken2);

                            // ✅ تاريخ يمين
                            row.ConstantItem(120).AlignRight().Text(printDate)
                               .FontSize(9).FontColor(Colors.Grey.Darken1);
                        });

                        if (!string.IsNullOrWhiteSpace(metaLine))
                        {
                            h.Item().PaddingTop(4).AlignRight()
                                .Text(CleanForXml(metaLine))
                                .FontSize(9).FontColor(Colors.Grey.Darken1);
                        }

                        h.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    });

                    // ===== Content =====
                    page.Content().PaddingTop(10).Element(content =>
                    {
                        if (propsRtl.Length == 0)
                        {
                            content.AlignCenter().PaddingTop(30)
                                   .Text("لا توجد بيانات/أعمدة للعرض")
                                   .FontSize(14).SemiBold().FontColor(Colors.Grey.Darken2);
                            return;
                        }

                        content.Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                foreach (var _ in propsRtl)
                                    cols.RelativeColumn();
                            });

                            // Header
                            table.Header(header =>
                            {
                                foreach (var p in propsRtl)
                                {
                                    var name = GetDisplayName(p.Name, options.ColumnMappings);

                                    header.Cell()
                                          .Background(Colors.Blue.Darken2)
                                          .PaddingVertical(6).PaddingHorizontal(4)
                                          .Border(1).BorderColor(Colors.Grey.Lighten2)
                                          .AlignCenter()
                                          .Text(CleanForXml(name))
                                          .FontColor(Colors.White)
                                          .FontSize(10)
                                          .SemiBold();
                                }
                            });

                            // Rows
                            for (int r = 0; r < list.Count; r++)
                            {
                                var item = list[r];
                                var bg = (r % 2 == 0) ? Colors.White : Colors.Grey.Lighten4;

                                foreach (var p in propsRtl)
                                {
                                    var raw = p.GetValue(item)?.ToString() ?? "";
                                    var val = CleanForXml(raw, null);

                                    table.Cell()
                                         .Background(bg)
                                         .PaddingVertical(5).PaddingHorizontal(4)
                                         .BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                                         .AlignCenter()
                                         .Text(val)
                                         .FontSize(9);
                                }
                            }
                        });
                    });

                    // ===== Footer =====
                    if (options.IncludeFooter)
                    {
                        page.Footer().PaddingTop(10).Column(f =>
                        {
                            f.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                            // ✅ بدون فواصل منقوطة غلط + chaining صحيح
                            f.Item().AlignCenter().Text(t =>
                            {
                                t.Span("صفحة ").FontSize(9).FontColor(Colors.Grey.Darken2);
                                t.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken2);
                                t.Span(" من ").FontSize(9).FontColor(Colors.Grey.Darken2);
                                t.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken2);
                            });
                        });
                    }
                });
            }).GeneratePdf();

            var safeName = MakeSafeFileName(options.ReportName, "Report");

            return Task.FromResult(new ExportFileDto
            {
                FileContents = pdfBytes,
                ContentType = "application/pdf",
                FileName = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
            });
        }

        // =========================
        // Helpers
        // =========================
        private string BuildMetaLine(ExportOptions o)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(o.BranchName)) parts.Add($"الفرع: {o.BranchName}");
            if (!string.IsNullOrWhiteSpace(o.DepartmentName)) parts.Add($"القسم: {o.DepartmentName}");
            if (!string.IsNullOrWhiteSpace(o.PeriodText)) parts.Add($"الفترة: {o.PeriodText}");
            return string.Join("  |  ", parts);
        }

        private void TryRegisterArabicFont()
        {
            var fontResId = "ArchiveProject.Infrastructure.Resources.Fonts.NotoNaskhArabic-Regular.ttf";
            try
            {
                using var s = _assembly.GetManifestResourceStream(fontResId);
                if (s != null)
                {
                    s.Position = 0;
                    QuestPDF.Drawing.FontManager.RegisterFont(s);
                }
            }
            catch { }
        }

        private void TryDrawLogo(QuestPDF.Fluent.RowDescriptor row)
        {
            if (string.IsNullOrWhiteSpace(_logoResId)) return;
            try
            {
                using var s = _assembly.GetManifestResourceStream(_logoResId);
                if (s != null)
                {
                    row.ConstantItem(48).AlignLeft().Image(s);
                    row.Spacing(8);
                }
            }
            catch { }
        }

        private void CreateHeader(IXLWorksheet ws, int colCount, string title, ExportOptions opt)
        {
            // Title
            var t = ws.Range(1, 1, 1, colCount);
            t.Merge();
            t.Value = CleanForXml(title);
            t.Style.Font.Bold = true;
            t.Style.Font.FontSize = 18;
            t.Style.Font.FontColor = XLColor.White;
            t.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            t.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A");
            t.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Row(1).Height = 28;

            // Meta
            var meta = BuildMetaLine(opt);
            var m = ws.Range(2, 1, 2, colCount);
            m.Merge();
            m.Value = CleanForXml($"تاريخ التقرير: {DateTime.Now:yyyy/MM/dd HH:mm}" + (string.IsNullOrWhiteSpace(meta) ? "" : $"  |  {meta}"));
            m.Style.Font.FontSize = 10;
            m.Style.Font.FontColor = XLColor.FromHtml("#374151");
            m.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            m.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
            m.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Row(2).Height = 20;

            ws.Row(3).Height = 6;
        }

        private int CreateColumnHeaders(IXLWorksheet ws, PropertyInfo[] props, ExportOptions opt)
        {
            int r = 4;
            for (int i = 0; i < props.Length; i++)
            {
                var cell = ws.Cell(r, i + 1);
                cell.Value = CleanForXml(GetDisplayName(props[i].Name, opt.ColumnMappings));

                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = 11;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#111827");

                cell.Style.Border.TopBorder = XLBorderStyleValues.Medium;
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
                cell.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.RightBorder = XLBorderStyleValues.Thin;

                cell.Style.Border.TopBorderColor = XLColor.FromHtml("#111827");
                cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#111827");
                cell.Style.Border.LeftBorderColor = XLColor.FromHtml("#374151");
                cell.Style.Border.RightBorderColor = XLColor.FromHtml("#374151");
            }
            ws.Row(r).Height = 24;
            return r;
        }

        private int FillRows<T>(IXLWorksheet ws, List<T> list, PropertyInfo[] props, int headerRow, ExportOptions opt)
        {
            int r = headerRow + 1;

            foreach (var item in list)
            {
                for (int c = 0; c < props.Length; c++)
                {
                    var v = props[c].GetValue(item);
                    var cell = ws.Cell(r, c + 1);

                    // قيم منسقة بسيطة
                    if (v is DateTime dt)
                    {
                        cell.Value = dt;
                        cell.Style.DateFormat.Format = "yyyy/mm/dd";
                    }
                    else
                    {
                        cell.Value = CleanForXml(v?.ToString());
                    }

                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                }
                ws.Row(r).Height = 20;
                r++;
            }

            return r;
        }

        private void AutoFit(IXLWorksheet ws, int colCount)
        {
            for (int c = 1; c <= colCount; c++)
            {
                ws.Column(c).AdjustToContents();
                ws.Column(c).Width = Math.Min(Math.Max(ws.Column(c).Width, 14), 40);
            }
        }

        private void CreateSummary(IXLWorksheet ws, List<object> data, PropertyInfo[] props, int startRow, ExportOptions opt)
        {
            var r = startRow + 2;

            var rg = ws.Range(r, 1, r, Math.Max(2, Math.Min(props.Length, 6)));
            rg.Merge();
            rg.Value = $"ملخص: إجمالي السجلات = {data.Count}";
            rg.Style.Font.Bold = true;
            rg.Style.Font.FontColor = XLColor.FromHtml("#065F46");
            rg.Style.Fill.BackgroundColor = XLColor.FromHtml("#ECFDF5");
            rg.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            rg.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            ws.Row(r).Height = 20;
        }

        private void AddFooter(IXLWorksheet ws, int row, int colCount)
        {
            var rg = ws.Range(row, 1, row, Math.Max(1, colCount));
            rg.Merge();
            rg.Value = "تم إنشاء هذا التقرير بواسطة نظام الأرشفة";
            rg.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            rg.Style.Font.FontSize = 9;
            rg.Style.Font.FontColor = XLColor.FromHtml("#6B7280");
            rg.Style.Fill.BackgroundColor = XLColor.FromHtml("#F9FAFB");
            rg.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            ws.Row(row).Height = 18;
        }

        private static string GetDisplayName(string propertyName, Dictionary<string, string>? map = null)
            => (map != null && map.TryGetValue(propertyName, out var v)) ? v : propertyName;

        private static string CleanForXml(string? input, int? maxLen = 32767)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var cleaned = Regex.Replace(input, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");
            if (maxLen.HasValue && cleaned.Length > maxLen.Value) cleaned = cleaned[..maxLen.Value];
            return cleaned;
        }

        private static string MakeValidExcelSheetName(string? name, string fallback)
        {
            var s = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
            foreach (var ch in new[] { ':', '\\', '/', '?', '*', '[', ']' })
                s = s.Replace(ch.ToString(), "");
            s = Regex.Replace(s, @"\s{2,}", " ").Trim();
            if (s.Length > 31) s = s[..31];
            return string.IsNullOrWhiteSpace(s) ? fallback : s;
        }

        private static string EnsureUniqueSheetName(XLWorkbook wb, string desired)
        {
            var name = desired; int i = 2;
            while (wb.Worksheets.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                var suffix = $" ({i})";
                var baseLen = Math.Max(1, 31 - suffix.Length);
                var baseName = desired.Length > baseLen ? desired[..baseLen] : desired;
                name = baseName + suffix;
                i++;
            }
            return name;
        }

        private static string MakeSafeFileName(string? name, string fallback)
        {
            var s = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
            foreach (var ch in Path.GetInvalidFileNameChars())
                s = s.Replace(ch.ToString(), "");
            s = Regex.Replace(s, @"\s{2,}", " ").Trim();
            return string.IsNullOrWhiteSpace(s) ? fallback : s;
        }
    }
}
