using System.Text.RegularExpressions;
using JOIN.Application.Interface.Email;

namespace JOIN.Application.Services.Email;

/// <summary>
/// SPEC 71 — the default <see cref="IEmailTemplateRenderer"/>. It knows nothing but the closed list of
/// <see cref="EmailTemplateVariables"/>: there is no expression engine, no conditionals and no loops
/// (spec decision of 2026-10-19).
/// </summary>
public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    /// <summary>Matches <c>{{Grupo.Campo}}</c>; the key is everything between the braces. </summary>
    private static readonly Regex VariablePattern = new(@"\{\{(?<key>[^{}]+)\}\}", RegexOptions.Compiled);

    public IReadOnlyList<string> ExtractVariables(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return VariablePattern.Matches(text)
            .Select(m => m.Groups["key"].Value.Trim())
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> Validate(string? subject, string? body) =>
        ExtractVariables(subject)
            .Concat(ExtractVariables(body))
            .Where(key => !EmailTemplateVariables.IsKnown(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public EmailTemplateRenderResult Render(string? subject, string? body, EmailTemplateContext context) =>
        new(RenderText(subject, context, encode: false), RenderText(body, context, encode: true));

    /// <summary>
    /// Replaces every known variable by its value. The body is HTML, so its values are HTML-encoded
    /// (the subject is plain text).
    /// </summary>
    private static string RenderText(string? text, EmailTemplateContext context, bool encode)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return VariablePattern.Replace(text, match =>
        {
            var key = match.Groups["key"].Value.Trim();
            var value = EmailTemplateVariables.ValueOf(key, context);
            if (value is null)
            {
                // Not part of the closed list: the template cannot be saved, so this only happens
                // with a hand-written row. The text is kept as it is.
                return match.Value;
            }

            return encode ? EncodeHtml(value) : value;
        });
    }

    /// <summary>
    /// Escapes only the characters that break the HTML of the body (<c>&lt;</c>, <c>&gt;</c>,
    /// <c>&amp;</c>, <c>&quot;</c> and <c>&#39;</c>). <see cref="WebUtility.HtmlEncode"/> is not used
    /// because it also escapes the accented characters as numeric entities, which would render
    /// <c>Renovaci&#243;n</c> in the mail.
    /// </summary>
    private static string EncodeHtml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
}
