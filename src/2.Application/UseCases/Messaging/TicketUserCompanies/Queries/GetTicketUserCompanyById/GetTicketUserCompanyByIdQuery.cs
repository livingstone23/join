using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetTicketUserCompanyById;

/// <summary>
/// Retrieves a single ticket-roster entry by id, scoped to the authenticated tenant.
/// </summary>
public record GetTicketUserCompanyByIdQuery(Guid Id, bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<TicketUserCompanyDto>>;
