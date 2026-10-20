using JOIN.Application.Services.Email;

namespace JOIN.Application.Interface.Email;

/// <summary>
/// SPEC 71 — replaces the closed list of variables of an email template by the values of the context.
/// The implementation lives in the Application layer and has no external dependency.
/// </summary>
public interface IEmailTemplateRenderer
{
    /// <summary>
    /// The variables used by a text, in the order they appear and without duplicates
    /// (<c>{{Persona.Nombre}}</c> → <c>Persona.Nombre</c>).
    /// </summary>
    IReadOnlyList<string> ExtractVariables(string? text);

    /// <summary>
    /// The variables of the subject and the body that are NOT part of the closed list
    /// (<see cref="EmailTemplateVariables"/>). Empty when the template is valid.
    /// </summary>
    IReadOnlyList<string> Validate(string? subject, string? body);

    /// <summary>
    /// The subject and the body with every known variable replaced by its value. A known variable
    /// without value becomes empty text; an unknown key is left untouched.
    /// </summary>
    EmailTemplateRenderResult Render(string? subject, string? body, EmailTemplateContext context);
}
