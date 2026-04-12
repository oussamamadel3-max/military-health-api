using ArchiveProject.Application.Common.Interfaces;
using Ganss.Xss;

namespace ArchiveProject.Infrastructure.Services.Sanitization;

public sealed class HtmlTextInputSanitizer : ITextInputSanitizer
{
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        s.AllowedAttributes.Clear();
        s.AllowedCssProperties.Clear();
        s.AllowedAtRules.Clear();
        s.AllowedSchemes.Clear();
        s.AllowDataAttributes = false;
        return s;
    }

    public string? SanitizePlainText(string? input)
    {
        if (input is null)
            return null;

        var trimmed = input.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        return Sanitizer.Sanitize(trimmed);
    }
}
