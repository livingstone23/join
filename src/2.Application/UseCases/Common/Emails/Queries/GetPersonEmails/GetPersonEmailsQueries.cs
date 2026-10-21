using JOIN.Application.Common;
using JOIN.Application.DTO.Common.Emails;
using MediatR;

namespace JOIN.Application.UseCases.Common.Emails.Queries;

/// <summary><c>GET /Persons/{id}/email-addresses</c> (SPEC 72): the valid e-mail addresses of the person, primary first.</summary>
public sealed record GetPersonEmailAddressesQuery(Guid PersonId) : IRequest<Response<IReadOnlyList<PersonEmailAddressDto>>>;

/// <summary>
/// <c>GET /Persons/{id}/emails/attachment-candidates</c> (SPEC 72): live documents of the products of the company
/// (<c>ILIKE</c> on the product or the document, max. 50) and, with <c>leadId</c>, the live documents of that lead.
/// </summary>
public sealed record GetPersonEmailAttachmentCandidatesQuery(Guid PersonId, Guid? LeadId = null, string? Search = null)
    : IRequest<Response<IReadOnlyList<PersonEmailAttachmentCandidateDto>>>;

/// <summary>Whose history is listed: a person, the person of a customer, or a lead.</summary>
public enum PersonEmailHistoryScope
{
    Person = 1,
    Customer = 2,
    Lead = 3
}

/// <summary>
/// <c>GET /Persons/{id}/emails</c>, <c>GET /Customers/{id}/emails</c> and <c>GET /Leads/{id}/emails</c> (SPEC 72):
/// the email history, newest first, paged.
/// </summary>
public sealed record GetPersonEmailsQuery(PersonEmailHistoryScope Scope, Guid Id, int PageNumber = 1, int PageSize = 20)
    : IRequest<Response<PagedResult<PersonEmailMessageListItemDto>>>;

/// <summary>
/// <c>GET /Persons/{id}/emails/{emailId}</c> and <c>GET /Leads/{id}/emails/{emailId}</c> (SPEC 72): the detail with the
/// body, the copies and the attachments.
/// </summary>
public sealed record GetPersonEmailQuery(PersonEmailHistoryScope Scope, Guid Id, Guid EmailId)
    : IRequest<Response<PersonEmailMessageDto>>;
