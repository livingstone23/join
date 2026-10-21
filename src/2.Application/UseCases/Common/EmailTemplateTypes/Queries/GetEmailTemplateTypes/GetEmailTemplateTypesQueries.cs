using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using MediatR;

namespace JOIN.Application.UseCases.Common.EmailTemplateTypes.Queries;

/// <summary>
/// <c>GET /EmailTemplateTypes</c>: the company's live catalog (optional inactive rows). A SuperAdmin may also ask
/// for the deleted rows, to restore them (SPEC 41-post).
/// </summary>
public sealed record GetEmailTemplateTypesQuery(bool? IncludeInactive = null, bool? IncludeDeleted = null) : IRequest<Response<IReadOnlyList<EmailTemplateTypeDto>>>;
