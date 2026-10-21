using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using MediatR;

namespace JOIN.Application.UseCases.Common.EmailTemplates.Queries;

/// <summary>
/// <c>GET /EmailTemplates</c>: the company's templates, paged and filtered. A SuperAdmin may also ask for the
/// deleted rows, to restore them (SPEC 41-post).
/// </summary>
public sealed record GetEmailTemplatesQuery(
    string? Search = null,
    Guid? EmailTemplateTypeId = null,
    bool? IsActive = null,
    int PageNumber = 1,
    int PageSize = 20,
    bool? IncludeDeleted = null) : IRequest<Response<PagedResult<EmailTemplateDto>>>;

/// <summary><c>GET /EmailTemplates/{id}</c>: the detail of one template (deleted ones only for a SuperAdmin who asks).</summary>
public sealed record GetEmailTemplateByIdQuery(Guid Id, bool? IncludeDeleted = null) : IRequest<Response<EmailTemplateDto>>;

/// <summary>
/// SPEC 71 — <c>GET /EmailTemplates/lookup</c>: the active templates of the company for the picker of
/// the sending modal. Open to any authenticated user of the company (the seller sends the mail, but
/// does not administer the templates).
/// </summary>
public sealed record LookupEmailTemplatesQuery(Guid? EmailTemplateTypeId = null, string? Search = null)
    : IRequest<Response<IReadOnlyList<EmailTemplateLookupItemDto>>>;

/// <summary>
/// SPEC 71 — <c>GET /EmailTemplates/variables</c>: the closed list of variables with its description,
/// for the editor of the template.
/// </summary>
public sealed record GetEmailTemplateVariablesQuery : IRequest<Response<IReadOnlyList<EmailTemplateVariableDto>>>;

