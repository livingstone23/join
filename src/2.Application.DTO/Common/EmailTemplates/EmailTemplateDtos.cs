using JOIN.Application.DTO.Common;

namespace JOIN.Application.DTO.Common.EmailTemplates;

/// <summary>
/// A classification of the company's email templates (SPEC 71, table <c>Admin.EmailTemplateTypes</c>).
/// </summary>
public sealed record EmailTemplateTypeDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// A reusable email template of the company (SPEC 71, table <c>Admin.EmailTemplates</c>). The body is
/// the sanitized HTML and the subject and the body hold the variables of
/// <c>EmailTemplateVariables</c>.
/// </summary>
public sealed record EmailTemplateDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid EmailTemplateTypeId { get; init; }
    public string EmailTemplateTypeName { get; init; } = string.Empty;
    public string EmailTemplateTypeCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    /// <summary>Last change of the template, for the "modified" column of the list (FE-46).</summary>
    public DateTime? ModifiedAt { get; init; }
}

/// <summary>
/// A template of the picker of the sending modal (SPEC 71, <c>GET /EmailTemplates/lookup</c>): the
/// active templates of the company, ordered by type.
/// </summary>
public sealed record EmailTemplateLookupItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public Guid EmailTemplateTypeId { get; init; }
    public string EmailTemplateTypeName { get; init; } = string.Empty;
    public string EmailTemplateTypeCode { get; init; } = string.Empty;
}

/// <summary>
/// The subject and the body of a template already rendered (SPEC 71,
/// <c>POST /EmailTemplates/{id}/render</c>).
/// </summary>
public sealed record EmailTemplateRenderedDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
}

/// <summary>
/// A supported variable and its description for the editor (SPEC 71,
/// <c>GET /EmailTemplates/variables</c>).
/// </summary>
public sealed record EmailTemplateVariableDto
{
    public string Key { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
