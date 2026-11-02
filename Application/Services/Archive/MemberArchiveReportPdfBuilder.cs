using Application.Dtos.Archive;
using Application.Dtos.ExportDtos;
using Application.Services.Export;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Application.Services.Archive;

/// <summary>
/// يبني PDF التقرير الطبي الكامل بمقاس A4 وقالب عربي RTL ثابت (FR-072 / BR-078).
/// </summary>
public static class MemberArchiveReportPdfBuilder
{
    private static readonly object FontLock = new();
    private static bool _fontsRegistered;

    /// <summary>
    /// يولّد بايتات PDF من عقد التقرير الكامل دون مسارات تخزين داخلية.
    /// </summary>
    public static ExportFileDto Build(MemberFullMedicalReportDto report, DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(report);

        QuestPDF.Settings.License = LicenseType.Community;
        RegisterBrandFonts();

        var generatedText = generatedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
        var fileNumber = report.Member.FileNumber.ToString();

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(ExportBrandTheme.PdfMargin);
                page.PageColor(ExportBrandTheme.PageBackground);
                page.DefaultTextStyle(x => x
                    .FontFamily(ExportBrandTheme.FontFamily)
                    .FontSize(ExportBrandTheme.PdfBodyFontSize)
                    .FontColor(ExportBrandTheme.Text));
                page.ContentFromRightToLeft();

                page.Header().Column(h =>
                {
                    h.Item().AlignCenter().Text("التقرير الطبي الكامل")
                        .FontSize(ExportBrandTheme.PdfTitleFontSize)
                        .SemiBold()
                        .FontColor(ExportBrandTheme.Primary);
                    h.Item().AlignCenter().Text($"رقم الأرشيف: {report.ArchiveNumber}")
                        .FontSize(ExportBrandTheme.PdfHeaderFontSize)
                        .FontColor(ExportBrandTheme.TextSecondary);
                    h.Item().PaddingTop(4).LineHorizontal(1).LineColor(ExportBrandTheme.Border);
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("صفحة ").FontSize(8).FontColor(ExportBrandTheme.TextMuted);
                    t.CurrentPageNumber().FontSize(8).FontColor(ExportBrandTheme.TextMuted);
                    t.Span(" من ").FontSize(8).FontColor(ExportBrandTheme.TextMuted);
                    t.TotalPages().FontSize(8).FontColor(ExportBrandTheme.TextMuted);
                    t.Span($"  |  طُبع في {generatedText}").FontSize(8).FontColor(ExportBrandTheme.TextMuted);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(8);

                    SectionTitle(col, "هوية الأرشيف والمركز");
                    Field(col, "الرقم الأرشيفي", report.ArchiveNumber);
                    Field(col, "تاريخ الأرشفة", report.ArchivedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
                    Field(col, "رمز المركز", report.ArchiveCenterCode ?? "—");
                    Field(col, "اسم المركز", report.ArchiveCenterNameAr ?? "—");

                    SectionTitle(col, "بيانات المنتسب");
                    var m = report.Member;
                    Field(col, "رقم الملف", fileNumber);
                    Field(col, "الاسم", m.FullName);
                    Field(col, "اسم الأم", m.MotherName);
                    Field(col, "رقم التجنيد", m.RecruitmentNumber);
                    Field(col, "تاريخ الميلاد", m.BirthDate.ToString("yyyy/MM/dd"));
                    Field(col, "زمرة الدم", m.BloodTypeNameAr ?? "—");
                    Field(col, "سكر الدم", m.BloodSugarMgDl?.ToString() ?? "—");
                    Field(col, "الطول/الوزن", $"{m.HeightCm?.ToString() ?? "—"} / {m.WeightKg?.ToString() ?? "—"}");
                    Field(col, "BMI", $"{m.Bmi?.ToString() ?? "—"} ({m.BmiCategoryNameAr ?? "—"})");

                    if (m.LastTransfer is not null)
                    {
                        SectionTitle(col, "آخر نقل بين المراكز");
                        Field(col, "من", m.LastTransfer.FromCenterNameAr ?? m.LastTransfer.FromCenterId.ToString("D"));
                        Field(col, "إلى", m.LastTransfer.ToCenterNameAr ?? m.LastTransfer.ToCenterId.ToString("D"));
                        Field(col, "السبب", m.LastTransfer.TransferReason);
                        Field(col, "التاريخ", m.LastTransfer.ExecutedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
                    }

                    SectionTitle(col, "تقييمات العيادات");
                    if (report.ClinicAssessments.Count == 0)
                    {
                        col.Item().Text("لا توجد تقييمات عيادات.").FontColor(ExportBrandTheme.TextMuted);
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2);
                                c.RelativeColumn(1.2f);
                                c.RelativeColumn(2);
                                c.RelativeColumn(2);
                            });
                            table.Header(header =>
                            {
                                foreach (var title in new[] { "العيادة", "النتيجة", "الطبيب", "التاريخ" })
                                {
                                    header.Cell()
                                        .Background(ExportBrandTheme.TableHeaderBackground)
                                        .Border(0.5f).BorderColor(ExportBrandTheme.TableBorder)
                                        .Padding(4).AlignRight()
                                        .Text(title).SemiBold().FontSize(9);
                                }
                            });
                            foreach (var row in report.ClinicAssessments.OrderBy(r => r.SortOrder))
                            {
                                foreach (var text in new[]
                                         {
                                             row.ClinicNameAr,
                                             row.IsMissing ? "ناقص" : (row.ResultStatusNameAr ?? "—"),
                                             row.AssessedByUserName ?? row.AssessedByUserId ?? "—",
                                             row.AssessedAt?.ToLocalTime().ToString("yyyy/MM/dd") ?? "—"
                                         })
                                {
                                    table.Cell()
                                        .Border(0.5f).BorderColor(ExportBrandTheme.RowBorder)
                                        .Padding(3).AlignRight()
                                        .Text(text).FontSize(8);
                                }
                            }
                        });
                    }

                    SectionTitle(col, "الاستشارات والاستقصاءات");
                    if (report.MedicalRequests.IsEmpty)
                    {
                        col.Item().Text(report.MedicalRequests.EmptyMessageAr ?? "لا توجد سجلات.")
                            .FontColor(ExportBrandTheme.TextMuted);
                    }
                    else
                    {
                        foreach (var item in report.MedicalRequests.Items)
                        {
                            col.Item().Text(
                                    $"{item.RequestKind}: {item.TypeNameAr ?? item.SpecialtyNameAr} — {item.Status}")
                                .SemiBold();
                            if (!string.IsNullOrWhiteSpace(item.ResultText))
                                col.Item().Text($"النتيجة: {item.ResultText}");
                            if (item.Attachments.Count > 0)
                                col.Item().Text($"مرفقات: {item.Attachments.Count} (تنزيل عبر الواجهة المحمية)")
                                    .FontSize(9)
                                    .FontColor(ExportBrandTheme.TextMuted);
                        }
                    }

                    SectionTitle(col, "الملاحظات العامة");
                    if (report.GeneralNotes.Count == 0)
                    {
                        col.Item().Text("لا توجد ملاحظات عامة.").FontColor(ExportBrandTheme.TextMuted);
                    }
                    else
                    {
                        foreach (var note in report.GeneralNotes)
                        {
                            col.Item().Text(note.NoteText);
                            col.Item().Text(
                                    $"— {note.AuthorUserName ?? note.AuthorUserId} / {note.AuthorClinicNameAr} / {note.WrittenAt.ToLocalTime():yyyy/MM/dd}")
                                .FontSize(8)
                                .FontColor(ExportBrandTheme.TextMuted);
                        }
                    }

                    SectionTitle(col, "الإجراءات المؤجلة");
                    if (report.DeferredActions.Count == 0)
                    {
                        col.Item().Text("لا توجد إجراءات مؤجلة.").FontColor(ExportBrandTheme.TextMuted);
                    }
                    else
                    {
                        foreach (var d in report.DeferredActions)
                        {
                            col.Item().Text(
                                $"{d.ActionTypeNameAr ?? d.ActionTypeId.ToString()} — {d.Status} — {d.DeferReason}");
                        }
                    }

                    SectionTitle(col, "القرار النهائي");
                    var fd = report.FinalDecision;
                    Field(col, "النتيجة", fd.ResultStatusNameAr ?? fd.ResultStatusId.ToString());
                    Field(col, "تاريخ القرار", fd.DecidedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
                    Field(col, "المشرف", fd.SupervisorUserName ?? fd.SupervisorUserId);
                    if (!string.IsNullOrWhiteSpace(fd.RejectionReason))
                        Field(col, "سبب الرفض", fd.RejectionReason);
                    if (!string.IsNullOrWhiteSpace(fd.DisagreementJustification))
                        Field(col, "مبرر الاختلاف", fd.DisagreementJustification);

                    if (report.DecisionHistory.Count > 0)
                    {
                        SectionTitle(col, "تاريخ تعديل القرار");
                        foreach (var hItem in report.DecisionHistory)
                        {
                            col.Item().Text(
                                $"{hItem.ChangedAt.ToLocalTime():yyyy/MM/dd HH:mm}: {hItem.OldStatusNameAr} ← {hItem.NewStatusNameAr} — {hItem.ChangeReason}");
                        }
                    }

                    if (!report.IsComplete && !string.IsNullOrWhiteSpace(report.IncompleteReasonAr))
                    {
                        col.Item().PaddingTop(6).Text(report.IncompleteReasonAr)
                            .FontColor(Colors.Red.Medium)
                            .SemiBold();
                    }
                });
            });
        }).GeneratePdf();

        return new ExportFileDto
        {
            FileContents = pdfBytes,
            ContentType = "application/pdf",
            FileName = $"medical-report-{report.ArchiveNumber}.pdf"
        };
    }

    private static void SectionTitle(ColumnDescriptor col, string title)
    {
        col.Item().PaddingTop(6).Text(title)
            .FontSize(12)
            .SemiBold()
            .FontColor(ExportBrandTheme.Primary);
        col.Item().LineHorizontal(0.6f).LineColor(ExportBrandTheme.TableBorder);
    }

    private static void Field(ColumnDescriptor col, string label, string value)
    {
        col.Item().Row(row =>
        {
            row.RelativeItem(2).AlignRight().Text($"{label}:")
                .FontColor(ExportBrandTheme.TextMuted);
            row.RelativeItem(5).AlignRight().Text(value ?? "—")
                .SemiBold();
        });
    }

    private static void RegisterBrandFonts()
    {
        if (_fontsRegistered)
            return;

        lock (FontLock)
        {
            if (_fontsRegistered)
                return;

            RegisterFontFile(ExportBrandTheme.FontRegularFileName);
            RegisterFontFile(ExportBrandTheme.FontBoldFileName);
            _fontsRegistered = true;
        }
    }

    private static void RegisterFontFile(string fileName)
    {
        using var stream = ExportEmbeddedResources.Open(fileName);
        if (stream is null)
            return;
        QuestPDF.Drawing.FontManager.RegisterFont(stream);
    }
}
