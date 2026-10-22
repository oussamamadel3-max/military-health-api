using Application.Dtos.ExportDtos;
using Application.Services.Export;
using Domain.Entities.Patient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ZXing;
using ZXing.Common;

namespace Application.Services.Members;

/// <summary>
/// يبني PDF إيصال المنتسب بمقاس ورق إيصال (80مم) مع باركود Code128 لرقم الملف وخط عربي RTL.
/// </summary>
public static class MemberReceiptPdfBuilder
{
    private static readonly object FontLock = new();
    private static bool _fontsRegistered;

    /// <summary>عرض ورق الإيصال المعتمد بالمليمتر.</summary>
    public const float ReceiptWidthMm = 80f;

    /// <summary>ارتفاع تقريبي للإيصال بالمليمتر (يتسع للمحتوى والباركود).</summary>
    public const float ReceiptHeightMm = 120f;

    /// <summary>
    /// يولّد بايتات PDF للإيصال من بيانات المنتسب المحمّلة (يشمل المركز الحالي).
    /// </summary>
    public static ExportFileDto Build(Member member, DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(member);

        QuestPDF.Settings.License = LicenseType.Community;
        RegisterBrandFonts();

        var fileNumberText = member.FileNumber.ToString();
        var centerName = member.CurrentMedicalCenter?.NameAr ?? string.Empty;
        var queueDateText = member.QueueDate.ToString("yyyy/MM/dd");
        var generatedText = generatedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
        var matrix = EncodeCode128(fileNumberText);

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(ReceiptWidthMm, ReceiptHeightMm, Unit.Millimetre);
                page.Margin(4, Unit.Millimetre);
                page.PageColor(ExportBrandTheme.PageBackground);
                page.DefaultTextStyle(x => x
                    .FontFamily(ExportBrandTheme.FontFamily)
                    .FontSize(9)
                    .FontColor(ExportBrandTheme.Text));
                page.ContentFromRightToLeft();

                page.Content().Column(col =>
                {
                    col.Spacing(3);

                    col.Item().AlignCenter().Text("إيصال المنتسب")
                        .FontSize(12)
                        .SemiBold()
                        .FontColor(ExportBrandTheme.Primary);

                    col.Item().PaddingTop(2).LineHorizontal(0.8f).LineColor(ExportBrandTheme.Border);

                    col.Item().Element(e => FieldRow(e, "رقم الملف", fileNumberText));
                    col.Item().Element(e => FieldRow(e, "رقم الدور", member.QueueNumber.ToString()));
                    col.Item().Element(e => FieldRow(e, "الاسم", member.FullName));
                    col.Item().Element(e => FieldRow(e, "المركز", centerName));
                    col.Item().Element(e => FieldRow(e, "تاريخ الدور", queueDateText));
                    col.Item().Element(e => FieldRow(e, "تاريخ الطباعة", generatedText));

                    col.Item().PaddingTop(4).LineHorizontal(0.8f).LineColor(ExportBrandTheme.Border);

                    col.Item().PaddingTop(4).AlignCenter().Height(28).Width(68, Unit.Millimetre)
                        .Element(c => DrawBarcode(c, matrix));

                    col.Item().AlignCenter().Text(fileNumberText)
                        .FontSize(10)
                        .SemiBold()
                        .FontColor(ExportBrandTheme.Text);
                });
            });
        }).GeneratePdf();

        return new ExportFileDto
        {
            FileContents = pdfBytes,
            ContentType = "application/pdf",
            FileName = $"member-receipt-{fileNumberText}.pdf"
        };
    }

    private static void FieldRow(IContainer container, string label, string value)
    {
        container.Row(row =>
        {
            row.RelativeItem(2).AlignRight().Text($"{label}:")
                .FontSize(8)
                .FontColor(ExportBrandTheme.TextMuted);
            row.RelativeItem(3).AlignRight().Text(value ?? string.Empty)
                .FontSize(9)
                .SemiBold()
                .FontColor(ExportBrandTheme.Text);
        });
    }

    private static BitMatrix EncodeCode128(string value)
    {
        var writer = new BarcodeWriterGeneric
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions
            {
                Height = 60,
                Width = 280,
                Margin = 0,
                PureBarcode = true
            }
        };
        return writer.Encode(value);
    }

    /// <summary>
    /// يرسم أعمدة الباركود كخلفيات أبيض/أسود داخل QuestPDF دون مكتبات صور.
    /// </summary>
    private static void DrawBarcode(IContainer container, BitMatrix matrix)
    {
        container.Row(row =>
        {
            row.Spacing(0);
            for (var x = 0; x < matrix.Width; x++)
            {
                var black = false;
                for (var y = 0; y < matrix.Height; y++)
                {
                    if (matrix[x, y])
                    {
                        black = true;
                        break;
                    }
                }

                var item = row.RelativeItem();
                if (black)
                    item.Background(Colors.Black);
                else
                    item.Background(Colors.White);
            }
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
