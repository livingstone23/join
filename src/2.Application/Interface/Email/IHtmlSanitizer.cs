namespace JOIN.Application.Interface.Email;

/// <summary>
/// SPEC 71 — sanitizes the HTML body of an email template before it is stored: no <c>script</c>, no
/// <c>iframe</c>, no <c>on*</c> event attributes and no <c>javascript:</c> URLs. The implementation
/// lives in the Infrastructure layer (Ganss <c>HtmlSanitizer</c>) and is shared with the email
/// sending flow of SPEC 72.
/// </summary>
public interface IHtmlSanitizer
{
    /// <summary>Returns the HTML without the dangerous parts; empty input returns empty output.</summary>
    string Sanitize(string? html);
}
