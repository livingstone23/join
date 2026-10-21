namespace JOIN.Application.Services.Email;

/// <summary>
/// SPEC 71 — the values the renderer substitutes for the closed list of variables. Every member is a
/// plain string: a variable without value (no lead, not a customer) renders as empty text.
/// </summary>
public sealed record EmailTemplateContext(
    string PersonaNombre,
    string PersonaNombreCompleto,
    string PersonaCorreo,
    string ClienteCodigo,
    string LeadCodigo,
    string LeadTema,
    string UsuarioNombre,
    string UsuarioCorreo,
    string EmpresaNombre,
    string FechaHoy)
{
    /// <summary>A context without values: every known variable renders as empty text.</summary>
    public static EmailTemplateContext Empty { get; } = new(
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
}

/// <summary>The subject and the body of a rendered template (SPEC 71).</summary>
public sealed record EmailTemplateRenderResult(string Subject, string BodyHtml);
