using System;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Commands.CreateTicketStatusTransition;

/// <summary>
/// Registers a new allowed transition between two ticket statuses for the current tenant.
/// </summary>
public record CreateTicketStatusTransitionCommand : ITransactionalCommand<Response<TicketStatusTransitionDto>>
{
    /// <summary>
    /// Gets the source status identifier.
    /// </summary>
    public Guid FromStatusId { get; init; }

    /// <summary>
    /// Gets the destination status identifier.
    /// </summary>
    public Guid ToStatusId { get; init; }
}