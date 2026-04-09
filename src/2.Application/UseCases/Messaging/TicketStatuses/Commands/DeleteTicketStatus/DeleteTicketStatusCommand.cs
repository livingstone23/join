using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;

/// <summary>
/// Command to perform a soft delete for a ticket status catalog item.
/// </summary>
/// <param name="Id">The ticket status identifier to delete.</param>
public sealed record DeleteTicketStatusCommand(Guid Id) : IRequest<Response<Guid>>;
