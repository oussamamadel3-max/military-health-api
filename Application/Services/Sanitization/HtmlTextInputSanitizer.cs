using Application.Interfaces.Services;
using System.Net;
using System.Text.RegularExpressions;

namespace Application.Services.Sanitization;

public sealed class HtmlTextInputSanitizer : ITextInputSanitizer
{
    private static readonly Regex ScriptOrStylePattern = new(
        @"<script[^>]*>.*?</script>|<style[^>]*>.*?</style>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HtmlTagPattern = new(
        @"<[^>]+>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string? SanitizePlainText(string? input)
    {
        if (input is null)
            return null;

        var trimmed = input.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        var withoutScriptsOrStyles = ScriptOrStylePattern.Replace(trimmed, string.Empty);
        var withoutTags = HtmlTagPattern.Replace(withoutScriptsOrStyles, string.Empty);
        var normalized = WhitespacePattern.Replace(withoutTags, " ").Trim();

        return WebUtility.HtmlEncode(normalized);
    }
}
