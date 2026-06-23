using Application.Interfaces.Services;
using Application.Dtos.ExportDtos;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Reflection;
using System.Text.RegularExpressions;


namespace Infrastructure.Services
{
    public class ExportService : IExportService
    {
        private readonly string _logoPath;

        private readonly Assembly _assembly;
        private readonly ILogger<ExportService>? _logger;


        private const string ArabicFontFileName = "Amiri-Regular.ttf";
        private const string ArabicFontFamily = "Amiri";
        private const string LogoFileName = "logo-192x192.png";

        private static bool _arabicFontRegistered = false;

        public ExportService()
        {
            _assembly = typeof(ExportService).Assembly;
            _logoPath = "Infrastructure.Resources.Images.logo-192x192.png";
        }

        // منشئ بديل في حال رغبت بالحقن عبر DI
        public ExportService(ILogger<ExportService> logger)
            : this()
        {
            _logger = logger;
        }


        #region ExportToExcel (خيارات موسعة)
        public async Task<ExportFileDto> ExportToExcelAsync<T>(IEnumerable<T> data, ExportOptions options)
        {
            using var workbook = new XLWorkbook();

            var rawSheet = string.IsNullOrWhiteSpace(options.SheetName) ? (options.ReportName ?? "تقرير") : options.SheetName!;
            var safeSheetName = EnsureUniqueSheetName(workbook, MakeValidExcelSheetName(rawSheet, "تقرير"));
            var safeReportName = MakeSafeFileName(options.ReportName ?? "Report");

            var worksheet = workbook.Worksheets.Add(safeSheetName);
            worksheet.RightToLeft = true;

            var props = typeof(T).GetProperties().ToArray();
            var dataList = data?.ToList() ?? new List<T>();

            if (options.ExcludedColumns?.Any() == true)
                props = props.Where(p => !options.ExcludedColumns.Contains(p.Name)).ToArray();

            if (props.Length == 0)
            {
                worksheet.Cell(1, 1).Value = CleanForXml(options.CustomTitle ?? $"تقرير {options.ReportName}");
                worksheet.Cell(2, 1).Value = "لا توجد بيانات للعرض";
                worksheet.Cell(3, 1).Value = $"تاريخ التقرير: {DateTime.Now:yyyy/MM/dd HH:mm}";

                using var emptyStream2 = new MemoryStream();
                workbook.SaveAs(emptyStream2);

                return new ExportFileDto
                {
                    FileContents = emptyStream2.ToArray(),
                    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    FileName = $"{safeReportName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };
            }

            var headerTitle = options.CustomTitle ?? $"تقرير {options.ReportName}";
            worksheet.PageSetup.Header.Center.AddText(CleanForXml(headerTitle));
            worksheet.PageSetup.Footer.Center.AddText("صفحة &P / &N");

            // عنوان
            CreateModernHeader(worksheet, props.Length, CleanForXml(headerTitle), options);

            // رؤوس الأعمدة
            var headerRow = CreateModernColumnHeaders(worksheet, props, options);

            // البيانات
            var dataRow = CreateModernDataRows(worksheet, dataList.Cast<object>().ToList(), props, headerRow, options);

            // إطار حول الجدول
            if (dataList.Any() && props.Length > 0)
            {
                var tableRange = worksheet.Range(headerRow, 1, dataRow - 1, props.Length);
                tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thick;
                tableRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#000000");
            }

            // تنسيق الأعمدة
            FormatModernColumns(worksheet, props.Length);

            // AutoFilter + Freeze
            if (dataList.Any() && props.Length > 0 && options.AutoFilter)
            {
                var dataRange = worksheet.Range(headerRow, 1, dataRow - 1, props.Length);
                dataRange.SetAutoFilter();
                if (options.FreezeHeaders)
                    worksheet.SheetView.FreezeRows(headerRow);
            }

            // ملخص (مع حماية)
            if (options.IncludeSummary)
            {
                try
                {
                    CreateModernSummary(worksheet, dataList.Cast<object>().ToList(), props, dataRow, options);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Summary block failed; continuing without summary.");
                }
            }

            // إطار خارجي
            var lastUsedRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
            var lastUsedColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 1;
            if (lastUsedRow > 0 && lastUsedColumn > 0)
            {
                var fullRange = worksheet.Range(1, 1, lastUsedRow, lastUsedColumn);
                fullRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thick;
                fullRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#000000");
            }

            // Footer داخلي
            if (options.IncludeFooter)
                AddFooterInfo(worksheet, dataRow + 10);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return new ExportFileDto
            {
                FileContents = stream.ToArray(),
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                FileName = $"{safeReportName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };
        }
        #endregion

        #region ExportToPdf
        public Task<ExportFileDto> ExportToPdfAsync<T>(IEnumerable<T> data, ExportOptions options)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            // ✅ لازم قبل أي GeneratePdf
            RegisterArabicFont();

            var props = typeof(T).GetProperties().ToArray();
            if (options.ExcludedColumns?.Any() == true)
                props = props.Where(p => !options.ExcludedColumns.Contains(p.Name)).ToArray();

            var list = data?.ToList() ?? new List<T>();

            // RTL عملي: نعكس الأعمدة
            var propsRtl = props.Reverse().ToArray();

            var titleText = CleanForXml(options.CustomTitle ?? options.ReportName);
            var printDate = DateTime.Now.ToString(options.DateFormat ?? "yyyy/MM/dd");
            var metaLine = BuildMetaLine(options);

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(22);

                    // ✅ خط عربي افتراضي لكل النصوص
                    page.DefaultTextStyle(x => x
                        .FontFamily(ArabicFontFamily)
                        .FontSize(11)
                        .FontColor(Colors.Grey.Darken4));

                    // ===== Header =====
                    page.Header().PaddingBottom(12).Column(h =>
                    {
                        h.Item().Row(row =>
                        {
                            // يسار: اللوغو
                            row.ConstantItem(45).AlignLeft().Element(e =>
                            {
                                if (options.IncludeLogo)
                                    TryDrawLogo(e);
                            });

                            // وسط: العنوان
                            row.RelativeItem().PaddingLeft(80).AlignCenter()
                                .Text(titleText)
                                .FontSize(22)
                                .SemiBold()
                                .FontColor(Colors.Blue.Darken2);

                            // يمين: كتلة التواريخ (فوق بعض)
                            row.ConstantItem(220).AlignRight().Column(c =>
                            {
                                c.Item().AlignRight()
                                    .Text($"تاريخ التقرير: {printDate}")
                                    .FontSize(10)
                                    .FontColor(Colors.Grey.Darken1);

                                if (!string.IsNullOrWhiteSpace(metaLine))
                                {
                                    c.Item().AlignRight()
                                        .Text($"الفترة: {CleanForXml(metaLine)}")
                                        .FontSize(10)
                                        .FontColor(Colors.Grey.Darken2);
                                }
                            });
                        });

                        // خط فاصل
                        h.Item().PaddingTop(10)
                            .LineHorizontal(1)
                            .LineColor(Colors.Grey.Lighten2);
                    });


                    // ===== Content =====
                    page.Content().PaddingTop(14).Element(content =>
                    {
                        if (propsRtl.Length == 0)
                        {
                            content.AlignCenter().PaddingTop(40)
                                .Text("لا توجد بيانات/أعمدة للعرض")
                                .FontSize(14)
                                .SemiBold()
                                .FontColor(Colors.Grey.Darken2);
                            return;
                        }

                        content.Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                foreach (var _ in propsRtl)
                                    cols.RelativeColumn();
                            });

                            // Table Header
                            table.Header(header =>
                            {
                                foreach (var p in propsRtl)
                                {
                                    var name = GetDisplayName(p.Name, options.ColumnMappings);

                                    header.Cell()
                                        .Background(Colors.Blue.Darken2)
                                        .PaddingVertical(7).PaddingHorizontal(6)
                                        .Border(1).BorderColor(Colors.Grey.Lighten2)
                                        .AlignCenter()
                                        .Text(CleanForXml(name))
                                        .FontColor(Colors.White)
                                        .FontSize(11)
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
                                    var raw = p.GetValue(item);
                                    var val = FormatPdfValue(raw, options);

                                    table.Cell()
                                        .Background(bg)
                                        .PaddingVertical(6).PaddingHorizontal(6)
                                        .AlignCenter()
                                        .Text(CleanForXml(val, null))
                                        .FontSize(10);
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

                            f.Item().AlignCenter().Text(t =>
                            {
                                t.Span("صفحة ").FontSize(10).FontColor(Colors.Grey.Darken2);
                                t.CurrentPageNumber().FontSize(10).FontColor(Colors.Grey.Darken2);
                                t.Span(" من ").FontSize(10).FontColor(Colors.Grey.Darken2);
                                t.TotalPages().FontSize(10).FontColor(Colors.Grey.Darken2);
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



        #region Helpers: Sheet/File name sanitizers
        private static string MakeValidExcelSheetName(string? name, string fallback = "Sheet1")
        {
            var s = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

            var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
            foreach (var ch in invalid) s = s.Replace(ch.ToString(), string.Empty);

            s = string.Concat(s.Where(c => !char.IsControl(c)));
            s = Regex.Replace(s, @"\s{2,}", " ").Trim();

            if (s.Length > 31) s = s.Substring(0, 31);
            return string.IsNullOrWhiteSpace(s) ? fallback : s;
        }

        private static string EnsureUniqueSheetName(XLWorkbook wb, string desired)
        {
            var name = desired;
            int i = 2;
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
            foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch.ToString(), string.Empty);
            s = Regex.Replace(s, @"\s{2,}", " ").Trim();
            return string.IsNullOrWhiteSpace(s) ? fallback : s;
        }

        // 7) تنسيقات وعناصر العرض
        private void CreateModernHeader(IXLWorksheet worksheet, int columnCount, string title, ExportOptions? options = null)
        {
            if (columnCount <= 0) columnCount = 1;

            var titleRow = 1;
            var titleRange = worksheet.Range(titleRow, 1, titleRow, columnCount);
            titleRange.Merge();
            titleRange.Value = CleanForXml(title);

            var themeColors = GetThemeColors(options?.Theme ?? ExportTheme.Modern);
            titleRange.Style.Font.FontSize = 18;
            titleRange.Style.Font.Bold = true;
            titleRange.Style.Font.FontColor = XLColor.White;
            titleRange.Style.Fill.BackgroundColor = themeColors.PrimaryColor;
            titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            titleRange.Style.Border.TopBorder = XLBorderStyleValues.Double;
            titleRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;
            titleRange.Style.Border.LeftBorder = XLBorderStyleValues.Double;
            titleRange.Style.Border.RightBorder = XLBorderStyleValues.Double;
            titleRange.Style.Border.TopBorderColor = themeColors.DarkColor;
            titleRange.Style.Border.BottomBorderColor = themeColors.DarkColor;
            titleRange.Style.Border.LeftBorderColor = themeColors.DarkColor;
            titleRange.Style.Border.RightBorderColor = themeColors.DarkColor;

            var infoRow = 2;
            var infoRange = worksheet.Range(infoRow, 1, infoRow, columnCount);
            infoRange.Merge();

            var infoText = $"📅 تاريخ التقرير: {DateTime.Now.ToString(options?.DateFormat ?? "yyyy/MM/dd HH:mm")}";
            
            infoText += "  تم إنشاؤه بواسطة: نظام الارشفة المتطور";

            infoRange.Value = CleanForXml(infoText);
            infoRange.Style.Font.FontSize = 10;
            infoRange.Style.Font.FontColor = XLColor.FromHtml("#666666");
            infoRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8F9FA");
            infoRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            infoRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            infoRange.Style.Border.BottomBorderColor = XLColor.FromHtml("#E9ECEF");

            worksheet.Row(3).Height = 8;
        }

        private int CreateModernColumnHeaders(IXLWorksheet worksheet, System.Reflection.PropertyInfo[] props, ExportOptions? options = null)
        {
            var headerRow = 4;
            var themeColors = GetThemeColors(options?.Theme ?? ExportTheme.Modern);

            if (props.Length == 0) return headerRow;

            for (int i = 0; i < props.Length; i++)
            {
                var cell = worksheet.Cell(headerRow, i + 1);
                cell.Value = CleanForXml(GetDisplayName(props[i].Name, options?.ColumnMappings));

                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = 11;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = themeColors.SecondaryColor;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                cell.Style.Border.TopBorder = XLBorderStyleValues.Double;
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Double;
                cell.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.RightBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.TopBorderColor = themeColors.DarkColor;
                cell.Style.Border.BottomBorderColor = themeColors.DarkColor;
                cell.Style.Border.LeftBorderColor = themeColors.LightColor;
                cell.Style.Border.RightBorderColor = themeColors.LightColor;
            }

            worksheet.Row(headerRow).Height = 25;
            return headerRow;
        }

        private int CreateModernDataRows(IXLWorksheet worksheet, List<object> dataList, System.Reflection.PropertyInfo[] props, int headerRow, ExportOptions? options = null)
        {
            int dataRow = headerRow + 1;

            if (props.Length == 0 || !dataList.Any()) return dataRow;

            foreach (var item in dataList)
            {
                for (int col = 0; col < props.Length; col++)
                {
                    var cell = worksheet.Cell(dataRow, col + 1);
                    var value = props[col].GetValue(item);

                    FormatCellValueAdvanced(cell, value ?? "", props[col].PropertyType, options);
                    ApplyModernCellStyling(cell, props[col].PropertyType, dataRow - headerRow, options);
                }
                dataRow++;
            }

            return dataRow;
        }

        private void ApplyModernCellStyling(IXLCell cell, Type propertyType, int rowIndex, ExportOptions? options = null)
        {
            var themeColors = GetThemeColors(options?.Theme ?? ExportTheme.Modern);
            XLColor backgroundColor;

            if (propertyType == typeof(decimal) || propertyType == typeof(decimal?) ||
                propertyType == typeof(double) || propertyType == typeof(double?) ||
                propertyType == typeof(int) || propertyType == typeof(int?))
            {
                backgroundColor = rowIndex % 2 == 0 ? themeColors.LightAccent : themeColors.VeryLightAccent;
            }
            else if (propertyType == typeof(DateTime) || propertyType == typeof(DateTime?))
            {
                backgroundColor = rowIndex % 2 == 0 ? themeColors.LightBlue : themeColors.VeryLightBlue;
            }
            else if (propertyType == typeof(bool) || propertyType == typeof(bool?))
            {
                backgroundColor = rowIndex % 2 == 0 ? themeColors.LightYellow : themeColors.VeryLightYellow;
            }
            else
            {
                backgroundColor = rowIndex % 2 == 0 ? themeColors.LightGray : XLColor.White;
            }

            cell.Style.Fill.BackgroundColor = backgroundColor;

            cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.RightBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.TopBorderColor = themeColors.LightColor;
            cell.Style.Border.BottomBorderColor = themeColors.DarkColor;
            cell.Style.Border.LeftBorderColor = themeColors.LightColor;
            cell.Style.Border.RightBorderColor = themeColors.DarkColor;

            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.Horizontal = GetAlignmentForType(propertyType);

            cell.Style.Font.FontName = "Segoe UI";
            cell.Style.Font.FontSize = 10;

            if (rowIndex == 1)
            {
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = themeColors.VeryLightGray;
            }
        }

        private XLAlignmentHorizontalValues GetAlignmentForType(Type propertyType)
        {
            if (propertyType == typeof(decimal) || propertyType == typeof(decimal?) ||
                propertyType == typeof(double) || propertyType == typeof(double?) ||
                propertyType == typeof(int) || propertyType == typeof(int?))
                return XLAlignmentHorizontalValues.Right;
            else if (propertyType == typeof(DateTime) || propertyType == typeof(DateTime?))
                return XLAlignmentHorizontalValues.Center;
            else
                return XLAlignmentHorizontalValues.Right;
        }

        private void FormatModernColumns(IXLWorksheet worksheet, int columnCount)
        {
            if (columnCount <= 0) return;

            for (int col = 1; col <= columnCount; col++)
            {
                worksheet.Column(col).AdjustToContents();
                worksheet.Column(col).Width = Math.Max(worksheet.Column(col).Width, 15);
                worksheet.Column(col).Width = Math.Min(worksheet.Column(col).Width, 30);
            }
        }

        private void CreateModernSummary(IXLWorksheet worksheet, List<object> dataList, System.Reflection.PropertyInfo[] props, int dataRow, ExportOptions? options = null)
        {
            if (!dataList.Any()) return;

            var summaryStartRow = dataRow + 3;
            var themeColors = GetThemeColors(options?.Theme ?? ExportTheme.Modern);

            var summaryTitleRange = worksheet.Range(summaryStartRow, 1, summaryStartRow, props.Length);
            summaryTitleRange.Merge();
            summaryTitleRange.Value = "ملخص التقرير والإحصائيات";
            summaryTitleRange.Style.Font.Bold = true;
            summaryTitleRange.Style.Font.FontSize = 14;
            summaryTitleRange.Style.Font.FontColor = XLColor.White;
            summaryTitleRange.Style.Fill.BackgroundColor = themeColors.SuccessColor;
            summaryTitleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            summaryTitleRange.Style.Border.TopBorder = XLBorderStyleValues.Double;
            summaryTitleRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;
            summaryTitleRange.Style.Border.LeftBorder = XLBorderStyleValues.Double;
            summaryTitleRange.Style.Border.RightBorder = XLBorderStyleValues.Double;
            summaryTitleRange.Style.Border.TopBorderColor = themeColors.DarkColor;
            summaryTitleRange.Style.Border.BottomBorderColor = themeColors.DarkColor;
            summaryTitleRange.Style.Border.LeftBorderColor = themeColors.DarkColor;
            summaryTitleRange.Style.Border.RightBorderColor = themeColors.DarkColor;

            var totalRow = summaryStartRow + 2;
            worksheet.Cell(totalRow, 1).Value = " إجمالي عدد السجلات : ";
            worksheet.Cell(totalRow, 1).Style.Font.Bold = true;
            worksheet.Cell(totalRow, 1).Style.Fill.BackgroundColor = themeColors.LightAccent;
            worksheet.Cell(totalRow, 2).Value = dataList.Count;
            worksheet.Cell(totalRow, 2).Style.Font.Bold = true;
            worksheet.Cell(totalRow, 2).Style.Fill.BackgroundColor = themeColors.LightAccent;
            worksheet.Cell(totalRow, 2).Style.NumberFormat.Format = "0";

            var numericColumns = new List<(string name, int index)>();
            // استخرج قائمة الأعمدة المستثناة من التجميع (مع جعلها كبيرة للتأكد من المقارنة)
            var nonSummarized = options?.NonSummarizedColumns?.Select(c => c.ToUpper()) ?? Enumerable.Empty<string>();
            for (int i = 0; i < props.Length; i++)
            {
                // 1. تحقق من النوع (باقي كما هو)
                if (props[i].PropertyType == typeof(decimal) || props[i].PropertyType == typeof(decimal?) ||
                    props[i].PropertyType == typeof(double) || props[i].PropertyType == typeof(double?) ||
                    props[i].PropertyType == typeof(int) || props[i].PropertyType == typeof(int?))
                {
                    // 2. تحقق من الاستثناء
                    if (!nonSummarized.Contains(props[i].Name.ToUpper()))
                    {
                        numericColumns.Add((props[i].Name, i + 1));
                    }
                }
            }

            if (numericColumns.Any())
            {
                totalRow += 2;
                foreach (var (columnName, columnIndex) in numericColumns)
                {
                    var values = dataList.Select(x => props[columnIndex - 1].GetValue(x))
                                         .Where(x => x != null)
                                         .Select(x => TryToDecimal(x!, out var dec) ? dec : (decimal?)null)
                                         .Where(x => x.HasValue)
                                         .Select(x => x!.Value)
                                         .ToList();

                    if (values.Any())
                    {
                        worksheet.Cell(totalRow, 1).Value = $" إجمالي {CleanForXml(GetDisplayName(columnName, options?.ColumnMappings))} :";
                        worksheet.Cell(totalRow, 1).Style.Font.Bold = true;
                        worksheet.Cell(totalRow, 1).Style.Fill.BackgroundColor = themeColors.LightBlue;

                        worksheet.Cell(totalRow, 2).Value = values.Sum();
                        worksheet.Cell(totalRow, 2).Style.NumberFormat.Format = options?.NumberFormat ?? "0.00;[Red]-0.00";
                        worksheet.Cell(totalRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        worksheet.Cell(totalRow, 2).Style.Font.Bold = true;
                        worksheet.Cell(totalRow, 2).Style.Fill.BackgroundColor = themeColors.LightBlue;
                        totalRow++;
                    }
                }
            }
        }

        private void AddFooterInfo(IXLWorksheet worksheet, int startRow)
        {
            var footerRow = startRow + 2;
            var footerRange = worksheet.Range(footerRow, 1, footerRow, 10);
            footerRange.Merge();
            footerRange.Value = " تم إنشاء هذا التقرير بواسطة نظام الارشفة المتطور  ";
            footerRange.Style.Font.FontSize = 9;
            footerRange.Style.Font.FontColor = XLColor.FromHtml("#6C757D");
            footerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8F9FA");
            footerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            footerRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            footerRange.Style.Border.TopBorderColor = XLColor.FromHtml("#E9ECEF");
        }

        private void FormatCellValueAdvanced(IXLCell cell, object value, Type propertyType, ExportOptions? options = null)
        {
            if (value == null) { cell.Value = ""; return; }

            var dateFormat = options?.DateFormat ?? "yyyy/MM/dd";
            var numberFormat = options?.NumberFormat ?? "0.00";

            switch (Type.GetTypeCode(propertyType))
            {
                case TypeCode.DateTime:
                    if (value is DateTime dt)
                    {
                        cell.Value = dt.ToString(dateFormat);
                        // ملاحظة: Excel format للأشهر هو m وليس M
                        cell.Style.DateFormat.Format = "yyyy/mm/dd";
                        cell.Style.Font.FontColor = XLColor.FromHtml("#0066CC");
                    }
                    break;

                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    if (decimal.TryParse(value.ToString(), out var dec))
                    {
                        cell.Value = dec;
                        cell.Style.NumberFormat.Format = numberFormat;
                        cell.Style.Font.FontColor = XLColor.FromHtml("#059669");
                        cell.Style.Font.Bold = true;

                        if (Math.Abs(dec) >= 1_000_000)
                        {
                            cell.Style.Font.FontColor = XLColor.FromHtml("#DC2626");
                            cell.Style.Font.Bold = true;
                        }
                        else if (Math.Abs(dec) >= 100_000)
                        {
                            cell.Style.Font.FontColor = XLColor.FromHtml("#D97706");
                            cell.Style.Font.Bold = true;
                        }
                    }
                    break;

                case TypeCode.Int32:
                case TypeCode.Int64:
                    if (long.TryParse(value.ToString(), out var lng))
                    {
                        cell.Value = lng;
                        cell.Style.NumberFormat.Format = "0";
                        cell.Style.Font.FontColor = XLColor.FromHtml("#17A2B8");
                    }
                    break;

                default:
                    // تنظيف نصوص من محارف التحكم + قصها لحد Excel + تخفيف Formula Injection في Excel
                    var s = value.ToString();
                    cell.Value = NeutralizeExcelFormulaInjection(CleanForXml(s));
                    cell.Style.Font.FontColor = XLColor.FromHtml("#495057");
                    break;
            }
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

        // 8) تنظيف النصوص والأرقام (جديد)
        private static string CleanForXml(string? input, int? maxLen = 32767)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                input,
                @"[\x00-\x08\x0B\x0C\x0E-\x1F]",
                string.Empty
            );

            if (maxLen.HasValue && cleaned.Length > maxLen.Value)
                cleaned = cleaned.Substring(0, maxLen.Value);

            return cleaned;
        }

        private static bool TryToDecimal(object obj, out decimal result)
        {
            switch (obj)
            {
                case decimal d: result = d; return true;
                case double db: result = (decimal)db; return true;
                case float f: result = (decimal)f; return true;
                case int i: result = i; return true;
                case long l: result = l; return true;
                case string s:
                    if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d1))
                    { result = d1; return true; }
                    if (decimal.TryParse(s, out var d2))
                    { result = d2; return true; }
                    break;
            }
            result = 0;
            return false;
        }

        // 9) الثيمات والألوان
        private ThemeColors GetThemeColors(ExportTheme theme)
        {
            return theme switch
            {
                ExportTheme.Modern => new ThemeColors
                {
                    PrimaryColor = XLColor.FromHtml("#2E86AB"),
                    SecondaryColor = XLColor.FromHtml("#495057"),
                    SuccessColor = XLColor.FromHtml("#28A745"),
                    DarkColor = XLColor.FromHtml("#1A5490"),
                    LightColor = XLColor.FromHtml("#6C757D"),
                    LightAccent = XLColor.FromHtml("#E8F8F5"),
                    VeryLightAccent = XLColor.FromHtml("#F0FDF4"),
                    LightBlue = XLColor.FromHtml("#EBF8FF"),
                    VeryLightBlue = XLColor.FromHtml("#F0F9FF"),
                    LightYellow = XLColor.FromHtml("#FFFBEB"),
                    VeryLightYellow = XLColor.FromHtml("#FFFDF7"),
                    LightGray = XLColor.FromHtml("#F9FAFB"),
                    VeryLightGray = XLColor.FromHtml("#F8FAFC")
                },
                ExportTheme.Classic => new ThemeColors
                {
                    PrimaryColor = XLColor.FromHtml("#6C757D"),
                    SecondaryColor = XLColor.FromHtml("#495057"),
                    SuccessColor = XLColor.FromHtml("#28A745"),
                    DarkColor = XLColor.FromHtml("#343A40"),
                    LightColor = XLColor.FromHtml("#ADB5BD"),
                    LightAccent = XLColor.FromHtml("#F8F9FA"),
                    VeryLightAccent = XLColor.FromHtml("#FFFFFF"),
                    LightBlue = XLColor.FromHtml("#E9ECEF"),
                    VeryLightBlue = XLColor.FromHtml("#F8F9FA"),
                    LightYellow = XLColor.FromHtml("#FFF3CD"),
                    VeryLightYellow = XLColor.FromHtml("#FFF8E1"),
                    LightGray = XLColor.FromHtml("#F8F9FA"),
                    VeryLightGray = XLColor.FromHtml("#FFFFFF")
                },
                ExportTheme.Minimal => new ThemeColors
                {
                    PrimaryColor = XLColor.FromHtml("#000000"),
                    SecondaryColor = XLColor.FromHtml("#6C757D"),
                    SuccessColor = XLColor.FromHtml("#28A745"),
                    DarkColor = XLColor.FromHtml("#000000"),
                    LightColor = XLColor.FromHtml("#E9ECEF"),
                    LightAccent = XLColor.FromHtml("#FFFFFF"),
                    VeryLightAccent = XLColor.FromHtml("#FFFFFF"),
                    LightBlue = XLColor.FromHtml("#FFFFFF"),
                    VeryLightBlue = XLColor.FromHtml("#FFFFFF"),
                    LightYellow = XLColor.FromHtml("#FFFFFF"),
                    VeryLightYellow = XLColor.FromHtml("#FFFFFF"),
                    LightGray = XLColor.FromHtml("#FFFFFF"),
                    VeryLightGray = XLColor.FromHtml("#FFFFFF")
                },
                ExportTheme.Corporate => new ThemeColors
                {
                    PrimaryColor = XLColor.FromHtml("#1E3A8A"),
                    SecondaryColor = XLColor.FromHtml("#374151"),
                    SuccessColor = XLColor.FromHtml("#059669"),
                    DarkColor = XLColor.FromHtml("#1E40AF"),
                    LightColor = XLColor.FromHtml("#9CA3AF"),
                    LightAccent = XLColor.FromHtml("#EFF6FF"),
                    VeryLightAccent = XLColor.FromHtml("#F8FAFC"),
                    LightBlue = XLColor.FromHtml("#DBEAFE"),
                    VeryLightBlue = XLColor.FromHtml("#F0F9FF"),
                    LightYellow = XLColor.FromHtml("#FEF3C7"),
                    VeryLightYellow = XLColor.FromHtml("#FFFBEB"),
                    LightGray = XLColor.FromHtml("#F9FAFB"),
                    VeryLightGray = XLColor.FromHtml("#F8FAFC")
                },
                ExportTheme.Financial => new ThemeColors
                {
                    PrimaryColor = XLColor.FromHtml("#059669"),
                    SecondaryColor = XLColor.FromHtml("#374151"),
                    SuccessColor = XLColor.FromHtml("#059669"),
                    DarkColor = XLColor.FromHtml("#047857"),
                    LightColor = XLColor.FromHtml("#9CA3AF"),
                    LightAccent = XLColor.FromHtml("#ECFDF5"),
                    VeryLightAccent = XLColor.FromHtml("#F0FDF4"),
                    LightBlue = XLColor.FromHtml("#DBEAFE"),
                    VeryLightBlue = XLColor.FromHtml("#F0F9FF"),
                    LightYellow = XLColor.FromHtml("#FEF3C7"),
                    VeryLightYellow = XLColor.FromHtml("#FFFBEB"),
                    LightGray = XLColor.FromHtml("#F9FAFB"),
                    VeryLightGray = XLColor.FromHtml("#F8FAFC")
                },
                _ => new ThemeColors()
            };
        }

        private class ThemeColors
        {
            public XLColor PrimaryColor { get; set; } = XLColor.FromHtml("#2E86AB");
            public XLColor SecondaryColor { get; set; } = XLColor.FromHtml("#495057");
            public XLColor SuccessColor { get; set; } = XLColor.FromHtml("#28A745");
            public XLColor DarkColor { get; set; } = XLColor.FromHtml("#1A5490");
            public XLColor LightColor { get; set; } = XLColor.FromHtml("#6C757D");
            public XLColor LightAccent { get; set; } = XLColor.FromHtml("#E8F8F5");
            public XLColor VeryLightAccent { get; set; } = XLColor.FromHtml("#F0FDF4");
            public XLColor LightBlue { get; set; } = XLColor.FromHtml("#EBF8FF");
            public XLColor VeryLightBlue { get; set; } = XLColor.FromHtml("#F0F9FF");
            public XLColor LightYellow { get; set; } = XLColor.FromHtml("#FFFBEB");
            public XLColor VeryLightYellow { get; set; } = XLColor.FromHtml("#FFFDF7");
            public XLColor LightGray { get; set; } = XLColor.FromHtml("#F9FAFB");
            public XLColor VeryLightGray { get; set; } = XLColor.FromHtml("#F8FAFC");
        }

        private string BuildMetaLine(ExportOptions o)
        {
            // المطلوب عندك غالباً: "الفترة: ...." فقط
            // إذا بدك تضيف فرع/قسم كمان خليهم
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(o.PeriodText))
                parts.Add($"{o.PeriodText}");

            // إذا بدك هذول يظهروا كمان (اختياري)
            if (!string.IsNullOrWhiteSpace(o.BranchName))
                parts.Add($"الفرع: {o.BranchName}");
            if (!string.IsNullOrWhiteSpace(o.DepartmentName))
                parts.Add($"القسم: {o.DepartmentName}");

            // خلينا الفترة أول شي باليمين
            return string.Join("   |   ", parts);
        }

        private string? FindResourceIdEndsWith(string fileName)
        {
            // بيلتقطه حتى لو اختلف namespace/resource path
            var names = _assembly.GetManifestResourceNames();
            return names.FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetDisplayName(string propertyName, Dictionary<string, string>? map = null)
            => (map != null && map.TryGetValue(propertyName, out var v)) ? v : propertyName;

        private string FormatPdfValue(object? value, ExportOptions opt)
        {
            if (value == null) return "";

            // ✅ DateTimeOffset (مثل AuditLog.Timestamp)
            if (value is DateTimeOffset dto)
                return dto.ToString(opt.DateFormat ?? "yyyy/MM/dd");

            // ✅ DateTime
            if (value is DateTime dt)
                return dt.ToString(opt.DateFormat ?? "yyyy/MM/dd");

            return value.ToString() ?? "";
        }

        private void RegisterArabicFont()
        {
            if (_arabicFontRegistered) return;

            try
            {
                var fontResId = FindResourceIdEndsWith(ArabicFontFileName);
                if (fontResId == null)
                {
                    _logger?.LogWarning("Arabic font resource not found: {File}", ArabicFontFileName);
                    return;
                }

                using var stream = _assembly.GetManifestResourceStream(fontResId);
                if (stream == null)
                {
                    _logger?.LogWarning("Arabic font stream is null: {ResId}", fontResId);
                    return;
                }

                stream.Position = 0;
                FontManager.RegisterFont(stream);
                _arabicFontRegistered = true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Arabic font registration failed.");
            }
        }

        private void TryDrawLogo(IContainer container)
        {
            try
            {
                var logoResId = FindResourceIdEndsWith(LogoFileName);
                if (logoResId == null)
                {
                    _logger?.LogWarning("Logo resource not found: {File}", LogoFileName);
                    return;
                }

                using var s = _assembly.GetManifestResourceStream(logoResId);
                if (s == null)
                {
                    _logger?.LogWarning("Logo stream is null: {ResId}", logoResId);
                    return;
                }
                s.Position = 0;
                container.Height(45).Width(45).Image(s);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Drawing logo failed.");
            }
        }

        #endregion
    }
}




