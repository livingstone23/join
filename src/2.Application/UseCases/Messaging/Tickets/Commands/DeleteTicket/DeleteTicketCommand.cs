using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands;

/// <summary>
/// Command used to soft-delete a ticket.
/// </summary>
/// <param name="Id">Ticket identifier.</param>
public record DeleteTicketCommand(Guid Id) : IRequest<Response<Guid>>;
