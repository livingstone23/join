using System;
using System.Collections.Generic;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Queries.GetTicketStatusTransitions;

/// <summary>
/// Lists the ticket status transition rules configured for the current tenant.
/// </summary>
public record GetTicketStatusTransitionsQuery : IRequest<Response<IEnumerable<TicketStatusTransitionDto>>>
{
    /// <summary>
    /// Optional filter — when set, only rules whose <c>FromStatusId</c> matches are returned.
    /// </summary>
    public Guid? FromStatusId { get; init; }
}