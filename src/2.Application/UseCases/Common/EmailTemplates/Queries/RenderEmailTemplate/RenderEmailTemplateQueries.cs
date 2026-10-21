using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using MediatR;

namespace JOIN.Application.UseCases.Common.EmailTemplates.Queries;

/// <summary>
/// SPEC 71 — <c>POST /EmailTemplates/{id}/render</c>: the subject and the body of an active template with the
/// variables replaced by the values of the person, the lead, the user and the company.
/// </summary>
public sealed record RenderEmailTemplateQuery(Guid TemplateId, Guid PersonId, string? EmailAddress = null, Guid? LeadId = null)
    : IRequest<Response<EmailTemplateRenderedDto>>;
