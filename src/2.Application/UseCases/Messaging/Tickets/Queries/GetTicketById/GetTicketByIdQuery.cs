using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries;

/// <summary>
/// Query used to retrieve a ticket by identifier for the current company.
/// </summary>
/// <param name="Id">Ticket identifier.</param>
public record GetTicketByIdQuery(Guid Id) : IRequest<Response<TicketDto>>;
