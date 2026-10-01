namespace Application.Services.Export;

/// <summary>
/// هوية بصرية مشتركة لتصدير PDF/Excel مستخرجة من ثيم الواجهة (styles.scss + _tables.scss).
/// </summary>
public static class ExportBrandTheme
{
    // من frontend-template/src/styles.scss (:root)
    public const string Primary = "#ab8854";
    public const string Text = "#1f2937";
    public const string TextSecondary = "#374151";
    public const string TextMuted = "#6b7280";
    public const string Border = "#e5e7eb";
    public const string Surface = "#ffffff";
    public const string PageBackground = "#ffffff";
    public const string HoverBackground = "#f9fafb";

    /// <summary>color-mix(primary 8%, white) — رأس الجداول في الواجهة.</summary>
    public const string TableHeaderBackground = "#f7f3ed";

    /// <summary>color-mix(primary 22%, #e5e7eb) — حدود الجداول.</summary>
    public const string TableBorder = "#d8d2ca";

    /// <summary>color-mix(primary 10%, #e5e7eb) — فواصل صفوف.</summary>
    public const string RowBorder = "#e3ddd4";

    /// <summary>color-mix(primary 6%, white) — تمييز صف بديل خفيف.</summary>
    public const string AltRowBackground = "#faf7f3";

    /// <summary>من style fill في logo.svg.</summary>
    public const string LogoAccent = "#988561";

    // من @font-face في styles.scss
    public const string FontFamily = "itfQomraArabic";
    public const string FontRegularFileName = "itfQomraArabic-Regular.otf";
    public const string FontBoldFileName = "itfQomraArabic-Bold.otf";

    public const string LogoSvgFileName = "logo.svg";
    public const string LogoPngFileName = "logo.png";

    public const float PdfMargin = 22f;
    public const float PdfLogoSize = 48f;
    public const float PdfTitleFontSize = 18f;
    public const float PdfBodyFontSize = 10.5f;
    public const float PdfHeaderFontSize = 10f;
    public const float ExcelLogoWidthPx = 72f;
    public const float ExcelLogoHeightPx = 72f;
}
