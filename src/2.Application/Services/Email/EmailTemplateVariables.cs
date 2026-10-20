namespace JOIN.Application.Services.Email;

/// <summary>
/// SPEC 71 — the closed list of variables an email template may use, with the description the editor
/// shows to the user. This class is the single source of truth: the renderer validates against it and
/// the <c>GET /EmailTemplates/variables</c> endpoint returns it.
/// </summary>
public static class EmailTemplateVariables
{
    public const string PersonaNombre = "Persona.Nombre";
    public const string PersonaNombreCompleto = "Persona.NombreCompleto";
    public const string PersonaCorreo = "Persona.Correo";
    public const string ClienteCodigo = "Cliente.Codigo";
    public const string LeadCodigo = "Lead.Codigo";
    public const string LeadTema = "Lead.Tema";
    public const string UsuarioNombre = "Usuario.Nombre";
    public const string UsuarioCorreo = "Usuario.Correo";
    public const string EmpresaNombre = "Empresa.Nombre";
    public const string FechaHoy = "Fecha.Hoy";

    /// <summary> Every supported variable, in the order the editor lists them. </summary>
    public static IReadOnlyList<EmailTemplateVariable> All { get; } =
    [
        new(PersonaNombre, "Nombre de la persona (o razón social)"),
        new(PersonaNombreCompleto, "Nombres y apellidos"),
        new(PersonaCorreo, "Correo destino elegido"),
        new(ClienteCodigo, "Código del cliente (vacío si no es cliente)"),
        new(LeadCodigo, "Código del lead asociado (vacío sin lead)"),
        new(LeadTema, "Tema del lead"),
        new(UsuarioNombre, "Nombre del usuario que envía"),
        new(UsuarioCorreo, "Correo del usuario que envía"),
        new(EmpresaNombre, "Nombre de la empresa"),
        new(FechaHoy, "Fecha de hoy (dd/MM/yyyy)")
    ];

    /// <summary> Whether the key belongs to the closed list (case-insensitive). </summary>
    public static bool IsKnown(string key) =>
        All.Any(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The value of a variable for the given context, or <c>null</c> when the key is not part of the
    /// closed list. A known variable without value renders as empty text.
    /// </summary>
    public static string? ValueOf(string key, EmailTemplateContext context)
    {
        if (string.Equals(key, PersonaNombre, StringComparison.OrdinalIgnoreCase)) return context.PersonaNombre;
        if (string.Equals(key, PersonaNombreCompleto, StringComparison.OrdinalIgnoreCase)) return context.PersonaNombreCompleto;
        if (string.Equals(key, PersonaCorreo, StringComparison.OrdinalIgnoreCase)) return context.PersonaCorreo;
        if (string.Equals(key, ClienteCodigo, StringComparison.OrdinalIgnoreCase)) return context.ClienteCodigo;
        if (string.Equals(key, LeadCodigo, StringComparison.OrdinalIgnoreCase)) return context.LeadCodigo;
        if (string.Equals(key, LeadTema, StringComparison.OrdinalIgnoreCase)) return context.LeadTema;
        if (string.Equals(key, UsuarioNombre, StringComparison.OrdinalIgnoreCase)) return context.UsuarioNombre;
        if (string.Equals(key, UsuarioCorreo, StringComparison.OrdinalIgnoreCase)) return context.UsuarioCorreo;
        if (string.Equals(key, EmpresaNombre, StringComparison.OrdinalIgnoreCase)) return context.EmpresaNombre;
        if (string.Equals(key, FechaHoy, StringComparison.OrdinalIgnoreCase)) return context.FechaHoy;
        return null;
    }
}

/// <summary>A supported variable and its description for the editor (SPEC 71).</summary>
public sealed record EmailTemplateVariable(string Key, string Description);
