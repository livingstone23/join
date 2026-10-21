using System.Text.RegularExpressions;
using JOIN.Application.Interface.Email;

namespace JOIN.Infrastructure.Email;

/// <summary>
/// SPEC 71 — the <see cref="IHtmlSanitizer"/> implementation. Two passes:
/// <list type="number">
/// <item><description>a pre-pass that drops the dangerous blocks (<c>script</c>, <c>iframe</c>,
/// <c>object</c>, <c>embed</c>) with their content, so no script text survives as plain text;</description></item>
/// <item><description>Ganss <c>Ganss.Xss.HtmlSanitizer</c>, which strips the <c>on*</c> event
/// attributes, the <c>javascript:</c> URLs and every tag outside its allow list.</description></item>
/// </list>
/// A new Ganss sanitizer is created per call: the library is not thread-safe and the wrapper is
/// registered as transient.
/// </summary>
public sealed class HtmlSanitizer : IHtmlSanitizer
{
    private static readonly Regex DangerousBlocks = new(
        @"<(?<tag>script|iframe|object|embed)\b[^>]*>.*?</\k<tag>\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DangerousSelfClosing = new(
        @"<(?<tag>script|iframe|object|embed)\b[^>]*/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutBlocks = DangerousBlocks.Replace(html, string.Empty);
        withoutBlocks = DangerousSelfClosing.Replace(withoutBlocks, string.Empty);

        var sanitizer = new Ganss.Xss.HtmlSanitizer();
        return sanitizer.Sanitize(withoutBlocks);
    }
}
