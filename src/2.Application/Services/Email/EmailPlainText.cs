using System.Net;
using System.Text.RegularExpressions;

namespace JOIN.Application.Services.Email;

/// <summary>
/// SPEC 72 — the plain-text excerpt of an email, used in the summary of the contact of the lead
/// (rule 4). The HTML is stripped, the entities are decoded and the white space is collapsed.
/// </summary>
public static partial class EmailPlainText
{
    [GeneratedRegex("<[^>]+>", RegexOptions.None, 2000)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+", RegexOptions.None, 2000)]
    private static partial Regex WhiteSpacePattern();

    /// <summary>The text of the HTML without tags, truncated to <paramref name="maxLength"/> characters.</summary>
    public static string Extract(string? html, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = WhiteSpacePattern().Replace(TagPattern().Replace(html, " "), " ").Trim();
        text = WebUtility.HtmlDecode(text);
        text = WhiteSpacePattern().Replace(text, " ").Trim();

        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
