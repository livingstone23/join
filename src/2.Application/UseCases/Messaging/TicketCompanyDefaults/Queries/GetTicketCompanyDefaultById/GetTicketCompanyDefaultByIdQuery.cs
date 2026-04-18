using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Queries;

/// <summary>
/// Query used to retrieve a tenant ticket default configuration by identifier.
/// </summary>
/// <param name="Id">Configuration identifier.</param>
public record GetTicketCompanyDefaultByIdQuery(Guid Id) : IRequest<Response<TicketCompanyDefaultDto>>;
