using JOIN.Domain.Audit;

namespace JOIN.Domain.Admin;

/// <summary>
/// SPEC 71 — a reusable email template of a company (table <c>Admin.EmailTemplates</c>). The subject and the
/// body hold the closed-list variables of <c>EmailTemplateVariables</c> (<c>{{Persona.Nombre}}</c> and friends);
/// the body is HTML sanitized before it is stored. The name is unique per company among live rows.
/// </summary>
public class EmailTemplate : BaseTenantEntity
{
    public const int MaxNameLength = 150;
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 50_000;

    public Guid EmailTemplateTypeId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string BodyHtml { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // --- Navigation Properties ---

    /// <summary> The classification of the template (SPEC 71, rule 2). </summary>
    public virtual EmailTemplateType EmailTemplateType { get; set; } = null!;
}
