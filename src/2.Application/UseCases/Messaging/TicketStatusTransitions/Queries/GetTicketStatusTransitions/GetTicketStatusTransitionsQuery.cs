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

    /// <summary>
    /// Gets whether deleted rows are included. Honored only for <c>SuperAdmin</c> (SPEC 41, <see cref="SoftDeleteVisibility"/>).
    /// </summary>
    public bool? IncludeDeleted { get; init; }

    /// <summary>
    /// Gets an optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).
    /// </summary>
    public Guid? CompanyId { get; init; }
}
