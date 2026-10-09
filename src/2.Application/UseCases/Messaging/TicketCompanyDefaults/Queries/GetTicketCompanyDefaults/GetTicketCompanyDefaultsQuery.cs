using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Queries;

/// <summary>
/// Query used to retrieve the active tenant ticket default configurations.
/// </summary>
public record GetTicketCompanyDefaultsQuery(bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<IReadOnlyCollection<TicketCompanyDefaultDto>>>;
