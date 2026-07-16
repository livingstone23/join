using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketComplexities.Commands;

/// <summary>
/// Command to perform a soft delete for a ticket complexity catalog item.
/// </summary>
/// <param name="Id">The ticket complexity identifier to delete.</param>
public sealed record DeleteTicketComplexityCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;
