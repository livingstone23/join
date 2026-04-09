using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Queries;

/// <summary>
/// Query to retrieve a ticket status catalog item by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the ticket status to retrieve.</param>
public sealed record GetTicketStatusByIdQuery(Guid Id) : IRequest<Response<TicketStatusDto>>;
