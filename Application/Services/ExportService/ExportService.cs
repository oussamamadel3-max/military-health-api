using Application.Dtos.ExportDtos;
using Application.Interfaces.Services;
using Application.Services.Export;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Application.Services
{
    public class ExportService : IExportService
    {
        private readonly ILogger<ExportService>? _logger;
        private static bool _fontsRegistered;
        private static readonly object FontLock = new();

        public ExportService()
        {
        }

        public ExportService(ILogger<ExportService> logger)
        {
            _logger = logger;
        }

        #region ExportToExcel
        public Task<ExportFileDto> ExportToExcelAsync<T>(IEnumerable<T> data, ExportOptions options)
        {
            using var workbook = new XLWorkbook();

            var rawSheet = string.IsNullOrWhiteSpace(options.SheetName)
                ? (options.ReportName ?? "تقرير")
                : options.SheetName!;
            var safeSheetName = EnsureUniqueSheetName(workbook, MakeValidExcelSheetName(rawSheet, "تقرير"));
            var safeReportName = MakeSafeFileName(options.ReportName ?? "Report");

            var worksheet = workbook.Worksheets.Add(safeSheetName);
            worksheet.RightToLeft = true;
            worksheet.Style.Font.FontName = ExportBrandTheme.FontFamily;
            worksheet.Style.Font.FontColor = XLColor.FromHtml(ExportBrandTheme.Text);

            var props = typeof(T).GetProperties().ToArray();
            var dataList = data?.ToList() ?? new List<T>();

            if (options.ExcludedColumns?.Count > 0)
                props = props.Where(p => !options.ExcludedColumns.Contains(p.Name)).ToArray();

            var title = CleanForXml(options.CustomTitle ?? $"تقرير {options.ReportName}");
            var printDate = DateTime.Now.ToString(options.DateFormat ?? "yyyy/MM/dd HH:mm");

            // صفوف الترويسة: شعار / عنوان / تاريخ — فوق نطاق البيانات (بدون دمج داخل الجدول)
            const int logoRow = 1;
            const int titleRow = 2;
            const int infoRow = 3;
            const int spacerRow = 4;
            var headerRow = 5;

            worksheet.Row(logoRow).Height = 58;
            worksheet.Row(titleRow).Height = 28;
            worksheet.Row(infoRow).Height = 20;
            worksheet.Row(spacerRow).Height = 8;

            if (options.IncludeLogo)
                TryAddExcelLogo(worksheet, logoRow);

            var colCount = Math.Max(props.Length, 1);
            var titleRange = worksheet.Range(titleRow, 1, titleRow, colCount);
            titleRange.Merge();
            titleRange.Value = title;
            titleRange.Style.Font.FontName = ExportBrandTheme.FontFamily;
            titleRange.Style.Font.FontSize = 16;
            titleRange.Style.Font.Bold = true;
            titleRange.Style.Font.FontColor = XLColor.FromHtml(ExportBrandTheme.Primary);
            titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            titleRange.Style.Fill.BackgroundColor = XLColor.FromHtml(ExportBrandTheme.Surface);

            var meta = BuildMetaLine(options);
            var infoText = string.IsNullOrWhiteSpace(meta)
                ? $"تاريخ التقرير: {printDate}"
                : $"تاريخ التقرير: {printDate}   |   الفترة: {CleanForXml(meta)}";

            var infoRange = worksheet.Range(infoRow, 1, infoRow, colCount);
            infoRange.Merge();
            infoRange.Value = infoText;
            infoRange.Style.Font.FontName = ExportBrandTheme.FontFamily;
            infoRange.Style.Font.FontSize = 10;
            infoRange.Style.Font.FontColor = XLColor.FromHtml(ExportBrandTheme.TextMuted);
            infoRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            infoRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            infoRange.Style.Border.BottomBorderColor = XLColor.FromHtml(ExportBrandTheme.TableBorder);

            if (props.Length == 0)
            {
                worksheet.Cell(headerRow, 1).Value = "لا توجد أعمدة للعرض";
                ApplyEmptyExcelMessage(worksheet, headerRow, 1);
            }
            else if (dataList.Count == 0)
            {
                WriteExcelColumnHeaders(worksheet, props, headerRow, options);
                worksheet.Cell(headerRow + 1, 1).Value = "لا توجد بيانات للعرض";
                var emptyRange = worksheet.Range(headerRow + 1, 1, headerRow + 1, props.Length);
                emptyRange.Merge();
                ApplyEmptyExcelMessage(worksheet, headerRow + 1, props.Length);
            }
            else
            {
                WriteExcelColumnHeaders(worksheet, props, headerRow, options);
                var dataEndRow = WriteExcelDataRows(worksheet, dataList.Cast<object>().ToList(), props, headerRow, options);

                var tableRange = worksheet.Range(headerRow, 1, dataEndRow - 1, props.Length);
                tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                tableRange.Style.Border.OutsideBorderColor = XLColor.FromHtml(ExportBrandTheme.TableBorder);
                tableRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                tableRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                if (options.AutoFilter)
                    tableRange.SetAutoFilter();

                if (options.FreezeHeaders)
                    worksheet.SheetView.FreezeRows(headerRow);

                FormatExcelColumns(worksheet, props);
                ConfigureExcelPrint(worksheet, props.Length, headerRow, dataEndRow - 1);

                if (options.IncludeSummary)
                {
                    try
                    {
                        WriteExcelSummary(worksheet, dataList.Cast<object>().ToList(), props, dataEndRow + 1, options);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Summary block failed; continuing without summary.");
                    }
                }
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return Task.FromResult(new ExportFileDto
            {
                FileContents = stream.ToArray(),
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                FileName = $"{safeReportName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            });
        }
        #endregion

        #region ExportToPdf
        public Task<ExportFileDto> ExportToPdfAsync<T>(IEnumerable<T> data, ExportOptions options)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            RegisterBrandFonts();

            var props = typeof(T).GetProperties().ToArray();
            if (options.ExcludedColumns?.Count > 0)
                props = props.Where(p => !options.ExcludedColumns.Contains(p.Name)).ToArray();

            var list = data?.ToList() ?? new List<T>();
            var titleText = CleanForXml(options.CustomTitle ?? options.ReportName);
            var printDate = DateTime.Now.ToString(options.DateFormat ?? "yyyy/MM/dd");
            var metaLine = BuildMetaLine(options);
            var pageSize = props.Length > 5 ? PageSizes.A4.Landscape() : PageSizes.A4;
            var svgContent = options.IncludeLogo
                ? ExportEmbeddedResources.ReadText(ExportBrandTheme.LogoSvgFileName)
                : null;

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(pageSize);
                    page.Margin(ExportBrandTheme.PdfMargin);
                    page.PageColor(ExportBrandTheme.PageBackground);
                    page.DefaultTextStyle(x => x
                        .FontFamily(ExportBrandTheme.FontFamily)
                        .FontSize(ExportBrandTheme.PdfBodyFontSize)
                        .FontColor(ExportBrandTheme.Text));

                    page.ContentFromRightToLeft();

                    page.Header().PaddingBottom(10).Column(h =>
                    {
                        h.Item().Row(row =>
                        {
                            // مع ContentFromRightToLeft: العنصر الأول يظهر يميناً
                            row.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().Text(titleText)
                                    .FontSize(ExportBrandTheme.PdfTitleFontSize)
                                    .SemiBold()
                                    .FontColor(ExportBrandTheme.Primary);

                                c.Item().PaddingTop(4).Text($"تاريخ التقرير: {printDate}")
                                    .FontSize(ExportBrandTheme.PdfHeaderFontSize)
                                    .FontColor(ExportBrandTheme.TextMuted);

                                if (!string.IsNullOrWhiteSpace(metaLine))
                                {
                                    c.Item().PaddingTop(2).Text($"الفترة: {CleanForXml(metaLine)}")
                                        .FontSize(ExportBrandTheme.PdfHeaderFontSize)
                                        .FontColor(ExportBrandTheme.TextSecondary);
                                }
                            });

                            row.ConstantItem(ExportBrandTheme.PdfLogoSize + 8).AlignLeft().Element(e =>
                            {
                                if (!string.IsNullOrWhiteSpace(svgContent))
                                {
                                    e.Height(ExportBrandTheme.PdfLogoSize)
                                        .Width(ExportBrandTheme.PdfLogoSize)
                                        .Svg(svgContent)
                                        .FitArea();
                                }
                                else
                                {
                                    TryDrawLogoPng(e);
                                }
                            });
                        });

                        h.Item().PaddingTop(8)
                            .LineHorizontal(1.2f)
                            .LineColor(ExportBrandTheme.TableBorder);
                    });

                    page.Content().PaddingTop(12).Element(content =>
                    {
                        if (props.Length == 0)
                        {
                            content.AlignCenter().PaddingTop(40)
                                .Text("لا توجد أعمدة للعرض")
                                .FontSize(13)
                                .SemiBold()
                                .FontColor(ExportBrandTheme.TextMuted);
                            return;
                        }

                        if (list.Count == 0)
                        {
                            content.Border(1).BorderColor(ExportBrandTheme.TableBorder)
                                .Background(ExportBrandTheme.TableHeaderBackground)
                                .Padding(16)
                                .AlignCenter()
                                .Text("لا توجد بيانات للعرض")
                                .FontSize(12)
                                .SemiBold()
                                .FontColor(ExportBrandTheme.TextSecondary);
                            return;
                        }

                        content.Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                foreach (var _ in props)
                                    cols.RelativeColumn();
                            });

                            // يتكرر تلقائياً على الصفحات التالية
                            table.Header(header =>
                            {
                                foreach (var p in props)
                                {
                                    var name = GetDisplayName(p.Name, options.ColumnMappings);
                                    header.Cell()
                                        .Background(ExportBrandTheme.TableHeaderBackground)
                                        .BorderBottom(1).BorderColor(ExportBrandTheme.TableBorder)
                                        .Border(0.5f).BorderColor(ExportBrandTheme.RowBorder)
                                        .PaddingVertical(8).PaddingHorizontal(6)
                                        .AlignCenter().AlignMiddle()
                                        .Text(CleanForXml(name))
                                        .SemiBold()
                                        .FontSize(ExportBrandTheme.PdfHeaderFontSize)
                                        .FontColor(ExportBrandTheme.TextMuted);
                                }
                            });

                            for (var r = 0; r < list.Count; r++)
                            {
                                var item = list[r];
                                var bg = (r % 2 == 0)
                                    ? ExportBrandTheme.Surface
                                    : ExportBrandTheme.AltRowBackground;

                                foreach (var p in props)
                                {
                                    var raw = p.GetValue(item);
                                    table.Cell()
                                        .Background(bg)
                                        .BorderBottom(0.5f).BorderColor(ExportBrandTheme.RowBorder)
                                        .PaddingVertical(8).PaddingHorizontal(6)
                                        .AlignCenter()
                                        .AlignMiddle()
                                        .Text(CleanForXml(FormatPdfValue(raw, options), null))
                                        .FontSize(ExportBrandTheme.PdfBodyFontSize)
                                        .FontColor(ExportBrandTheme.Text);
                                }
                            }
                        });
                    });

                    if (options.IncludeFooter)
                    {
                        page.Footer().PaddingTop(8).Column(f =>
                        {
                            f.Item().LineHorizontal(1).LineColor(ExportBrandTheme.TableBorder);
                            f.Item().PaddingTop(6).AlignCenter().Text(t =>
                            {
                                t.DefaultTextStyle(x => x
                                    .FontFamily(ExportBrandTheme.FontFamily)
                                    .FontSize(9)
                                    .FontColor(ExportBrandTheme.TextMuted));
                                t.Span("صفحة ");
                                t.CurrentPageNumber();
                                t.Span(" من ");
                                t.TotalPages();
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
        #endregion

        #region QuickExport
        public Task<ExportFileDto> QuickExportToExcelAsync<T>(IEnumerable<T> data, string reportName)
            => ExportToExcelAsync(data, new ExportOptions
            {
                ReportName = reportName,
                SheetName = reportName,
                CustomTitle = reportName,
                IncludeSummary = true,
                IncludeFooter = true,
                IncludeLogo = true,
                AutoFilter = true,
                FreezeHeaders = true
            });

        public Task<ExportFileDto> QuickExportToPdfAsync<T>(IEnumerable<T> data, string reportName)
            => ExportToPdfAsync(data, new ExportOptions
            {
                ReportName = reportName,
                CustomTitle = reportName,
                IncludeLogo = true,
                IncludeFooter = true
            });
        #endregion

        #region Excel helpers
        private void TryAddExcelLogo(IXLWorksheet worksheet, int row)
        {
            try
            {
                var bytes = ExportEmbeddedResources.ReadBytes(ExportBrandTheme.LogoPngFileName);
                if (bytes is null || bytes.Length == 0)
                {
                    _logger?.LogWarning("Excel logo PNG resource missing: {File}", ExportBrandTheme.LogoPngFileName);
                    return;
                }

                using var ms = new MemoryStream(bytes);
                var picture = worksheet.AddPicture(ms)
                    .MoveTo(worksheet.Cell(row, 1), 4, 4)
                    .WithSize(
                        (int)ExportBrandTheme.ExcelLogoWidthPx,
                        (int)ExportBrandTheme.ExcelLogoHeightPx);
                _ = picture;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Adding Excel logo failed.");
            }
        }

        private static void WriteExcelColumnHeaders(
            IXLWorksheet worksheet,
            PropertyInfo[] props,
            int headerRow,
            ExportOptions options)
        {
            for (var i = 0; i < props.Length; i++)
            {
                var cell = worksheet.Cell(headerRow, i + 1);
                cell.Value = CleanForXml(GetDisplayName(props[i].Name, options.ColumnMappings));
                cell.Style.Font.FontName = ExportBrandTheme.FontFamily;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = 10;
                cell.Style.Font.FontColor = XLColor.FromHtml(ExportBrandTheme.TextMuted);
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(ExportBrandTheme.TableHeaderBackground);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.BottomBorderColor = XLColor.FromHtml(ExportBrandTheme.TableBorder);
                cell.Style.Border.LeftBorder = XLBorderStyleValues.Hair;
                cell.Style.Border.RightBorder = XLBorderStyleValues.Hair;
                cell.Style.Border.LeftBorderColor = XLColor.FromHtml(ExportBrandTheme.RowBorder);
                cell.Style.Border.RightBorderColor = XLColor.FromHtml(ExportBrandTheme.RowBorder);
            }

            worksheet.Row(headerRow).Height = 24;
        }

        private static int WriteExcelDataRows(
            IXLWorksheet worksheet,
            List<object> dataList,
            PropertyInfo[] props,
            int headerRow,
            ExportOptions options)
        {
            var dataRow = headerRow + 1;
            foreach (var item in dataList)
            {
                var rowIndex = dataRow - headerRow;
                for (var col = 0; col < props.Length; col++)
                {
                    var cell = worksheet.Cell(dataRow, col + 1);
                    FormatCellValue(cell, props[col].GetValue(item), props[col].PropertyType, options);
                    cell.Style.Font.FontName = ExportBrandTheme.FontFamily;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Font.FontColor = XLColor.FromHtml(ExportBrandTheme.Text);
                    cell.Style.Fill.BackgroundColor = rowIndex % 2 == 0
                        ? XLColor.FromHtml(ExportBrandTheme.Surface)
                        : XLColor.FromHtml(ExportBrandTheme.AltRowBackground);
                    cell.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
                    cell.Style.Border.BottomBorderColor = XLColor.FromHtml(ExportBrandTheme.RowBorder);
                    cell.Style.Border.LeftBorder = XLBorderStyleValues.Hair;
                    cell.Style.Border.RightBorder = XLBorderStyleValues.Hair;
                    cell.Style.Border.LeftBorderColor = XLColor.FromHtml(ExportBrandTheme.RowBorder);
                    cell.Style.Border.RightBorderColor = XLColor.FromHtml(ExportBrandTheme.RowBorder);
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Alignment.WrapText = false;
                }

                worksheet.Row(dataRow).Height = 22;
                dataRow++;
            }

            return dataRow;
        }

        private static void FormatExcelColumns(IXLWorksheet worksheet, PropertyInfo[] props)
        {
            for (var col = 1; col <= props.Length; col++)
            {
                worksheet.Column(col).AdjustToContents(5, 120);
                var width = worksheet.Column(col).Width;
                // حد أدنى أوضح وحد أعلى أوسع للجداول العربية
                worksheet.Column(col).Width = Math.Clamp(width, 18, 40);
            }
        }

        private static void ConfigureExcelPrint(IXLWorksheet worksheet, int columnCount, int headerRow, int lastDataRow)
        {
            worksheet.PageSetup.PageOrientation = columnCount > 5
                ? XLPageOrientation.Landscape
                : XLPageOrientation.Portrait;
            worksheet.PageSetup.FitToPages(1, 0);
            worksheet.PageSetup.Margins.Left = 0.4;
            worksheet.PageSetup.Margins.Right = 0.4;
            worksheet.PageSetup.Margins.Top = 0.5;
            worksheet.PageSetup.Margins.Bottom = 0.5;
            worksheet.PageSetup.SetRowsToRepeatAtTop($"{headerRow}:{headerRow}");
            worksheet.PageSetup.PrintAreas.Clear();
            worksheet.PageSetup.PrintAreas.Add($"$A$1:${GetExcelColumnName(Math.Max(columnCount, 1))}${Math.Max(lastDataRow, headerRow)}");
            worksheet.PageSetup.Header.Center.Clear();
            worksheet.PageSetup.Footer.Center.AddText("صفحة &P من &N");
        }

        private static string GetExcelColumnName(int columnNumber)
        {
            var dividend = columnNumber;
            var columnName = string.Empty;
            while (dividend > 0)
            {
                var modulo = (dividend - 1) % 26;
                columnName = Convert.ToChar('A' + modulo) + columnName;
                dividend = (dividend - modulo) / 26;
            }

            return columnName;
        }

        private static void ApplyEmptyExcelMessage(IXLWorksheet worksheet, int row, int columnSpan)
        {
            var range = columnSpan <= 1
                ? worksheet.Range(row, 1, row, 1)
                : worksheet.Range(row, 1, row, columnSpan);

            range.Style.Font.FontName = ExportBrandTheme.FontFamily;
            range.Style.Font.FontSize = 11;
            range.Style.Font.Bold = true;
            range.Style.Font.FontColor = XLColor.FromHtml(ExportBrandTheme.TextSecondary);
            range.Style.Fill.BackgroundColor = XLColor.FromHtml(ExportBrandTheme.TableHeaderBackground);
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.OutsideBorderColor = XLColor.FromHtml(ExportBrandTheme.TableBorder);
            worksheet.Row(row).Height = 28;
        }

        private static void WriteExcelSummary(
            IXLWorksheet worksheet,
            List<object> dataList,
            PropertyInfo[] props,
            int startRow,
            ExportOptions options)
        {
            if (dataList.Count == 0 || props.Length == 0)
                return;

            var row = startRow + 1;
            worksheet.Cell(row, 1).Value = "إجمالي عدد السجلات";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 1).Style.Font.FontName = ExportBrandTheme.FontFamily;
            worksheet.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml(ExportBrandTheme.Text);
            worksheet.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml(ExportBrandTheme.TableHeaderBackground);
            worksheet.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Cell(row, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            worksheet.Cell(row, 2).Value = dataList.Count;
            worksheet.Cell(row, 2).Style.Font.Bold = true;
            worksheet.Cell(row, 2).Style.NumberFormat.Format = "0";
            worksheet.Cell(row, 2).Style.Fill.BackgroundColor = XLColor.FromHtml(ExportBrandTheme.TableHeaderBackground);
            worksheet.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Cell(row, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // لا نجمع أعمدة المعرفات أو التواريخ — فقط إجمالي العدد أعلاه
            _ = options;
            _ = props;
        }

        private static void FormatCellValue(IXLCell cell, object? value, Type propertyType, ExportOptions options)
        {
            if (value is null)
            {
                cell.Value = Blank.Value;
                return;
            }

            var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            if (underlying == typeof(DateTimeOffset) && value is DateTimeOffset dto)
            {
                cell.Value = dto.ToString(options.DateFormat ?? "yyyy/MM/dd HH:mm");
                cell.Style.NumberFormat.Format = "@";
                return;
            }

            if (underlying == typeof(DateTime) && value is DateTime dt)
            {
                cell.Value = dt.ToString(options.DateFormat ?? "yyyy/MM/dd HH:mm");
                cell.Style.NumberFormat.Format = "@";
                return;
            }

            if (underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float))
            {
                if (TryToDecimal(value, out var dec))
                {
                    cell.Value = dec;
                    cell.Style.NumberFormat.Format = options.NumberFormat ?? "#,##0.00";
                    return;
                }
            }

            if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short))
            {
                // المعرفات الطويلة كنص لمنع التقريب العلمي وظهور #####
                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                if (text.Length > 12)
                {
                    cell.Value = text;
                    cell.Style.NumberFormat.Format = "@";
                    return;
                }

                if (long.TryParse(text, out var lng))
                {
                    cell.Value = lng;
                    cell.Style.NumberFormat.Format = "0";
                    return;
                }
            }

            if (underlying == typeof(bool) && value is bool b)
            {
                cell.Value = b ? "نعم" : "لا";
                return;
            }

            cell.Value = NeutralizeExcelFormulaInjection(CleanForXml(Convert.ToString(value, CultureInfo.CurrentCulture)));
            cell.Style.NumberFormat.Format = "@";
        }

        #endregion

        #region PDF / shared helpers
        private static string MakeValidExcelSheetName(string? name, string fallback = "Sheet1")
        {
            var s = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
            foreach (var ch in new[] { ':', '\\', '/', '?', '*', '[', ']' })
                s = s.Replace(ch.ToString(), string.Empty);

            s = string.Concat(s.Where(c => !char.IsControl(c)));
            s = Regex.Replace(s, @"\s{2,}", " ").Trim();
            if (s.Length > 31) s = s[..31];
            return string.IsNullOrWhiteSpace(s) ? fallback : s;
        }

        private static string EnsureUniqueSheetName(XLWorkbook wb, string desired)
        {
            var name = desired;
            var i = 2;
            while (wb.Worksheets.Any(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                var suffix = $" ({i})";
                var maxBase = 31 - suffix.Length;
                var baseName = desired.Length > maxBase ? desired[..maxBase] : desired;
                name = baseName + suffix;
                i++;
            }

            return name;
        }

        private static string MakeSafeFileName(string? name, string fallback = "Report")
        {
            var s = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
            foreach (var ch in Path.GetInvalidFileNameChars())
                s = s.Replace(ch.ToString(), string.Empty);
            s = Regex.Replace(s, @"\s{2,}", " ").Trim();
            return string.IsNullOrWhiteSpace(s) ? fallback : s;
        }

        private static string NeutralizeExcelFormulaInjection(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            var t = text.TrimStart();
            if (t.Length == 0)
                return text;
            var c = t[0];
            if (c is '=' or '+' or '-' or '@' or '\t')
                return "'" + text;
            return text;
        }

        private static string CleanForXml(string? input, int? maxLen = 32767)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var cleaned = Regex.Replace(input, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", string.Empty);
            if (maxLen.HasValue && cleaned.Length > maxLen.Value)
                cleaned = cleaned[..maxLen.Value];
            return cleaned;
        }

        private static bool TryToDecimal(object obj, out decimal result)
        {
            switch (obj)
            {
                case decimal d:
                    result = d;
                    return true;
                case double db:
                    result = (decimal)db;
                    return true;
                case float f:
                    result = (decimal)f;
                    return true;
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                case string s:
                    if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d1))
                    {
                        result = d1;
                        return true;
                    }

                    if (decimal.TryParse(s, out var d2))
                    {
                        result = d2;
                        return true;
                    }

                    break;
            }

            result = 0;
            return false;
        }

        private static string BuildMetaLine(ExportOptions o)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(o.PeriodText))
                parts.Add(o.PeriodText!);
            if (!string.IsNullOrWhiteSpace(o.BranchName))
                parts.Add($"الفرع: {o.BranchName}");
            if (!string.IsNullOrWhiteSpace(o.DepartmentName))
                parts.Add($"القسم: {o.DepartmentName}");
            return string.Join("   |   ", parts);
        }

        private static string GetDisplayName(string propertyName, Dictionary<string, string>? map = null)
            => map != null && map.TryGetValue(propertyName, out var v) ? v : propertyName;

        private static string FormatPdfValue(object? value, ExportOptions opt)
        {
            if (value is null) return "";
            if (value is DateTimeOffset dto)
                return dto.ToString(opt.DateFormat ?? "yyyy/MM/dd HH:mm");
            if (value is DateTime dt)
                return dt.ToString(opt.DateFormat ?? "yyyy/MM/dd HH:mm");
            if (value is bool b)
                return b ? "نعم" : "لا";
            return value.ToString() ?? "";
        }

        private void RegisterBrandFonts()
        {
            if (_fontsRegistered)
                return;

            lock (FontLock)
            {
                if (_fontsRegistered)
                    return;

                try
                {
                    RegisterFontFile(ExportBrandTheme.FontRegularFileName);
                    RegisterFontFile(ExportBrandTheme.FontBoldFileName);
                    _fontsRegistered = true;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Brand font registration failed.");
                }
            }
        }

        private void RegisterFontFile(string fileName)
        {
            using var stream = ExportEmbeddedResources.Open(fileName);
            if (stream is null)
            {
                _logger?.LogWarning("Font resource not found: {File}", fileName);
                return;
            }

            QuestPDF.Drawing.FontManager.RegisterFont(stream);
        }

        private void TryDrawLogoPng(IContainer container)
        {
            try
            {
                var bytes = ExportEmbeddedResources.ReadBytes(ExportBrandTheme.LogoPngFileName);
                if (bytes is null)
                    return;

                container.Height(ExportBrandTheme.PdfLogoSize)
                    .Width(ExportBrandTheme.PdfLogoSize)
                    .Image(bytes)
                    .FitArea();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Drawing PNG logo fallback failed.");
            }
        }
        #endregion
    }
}
