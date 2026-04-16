using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands;

/// <summary>
/// Command used to create a ticket in the current tenant context.
/// </summary>
public record CreateTicketCommand : IRequest<Response<TicketDto>>
{
    /// <summary>
    /// Gets or sets the ticket title.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the ticket description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the estimated time.
    /// </summary>
    public decimal EstimatedTime { get; init; }

    /// <summary>
    /// Gets or sets the consumed time.
    /// </summary>
    public decimal ConsumedTime { get; init; }

    /// <summary>
    /// Gets or sets the optional effort points.
    /// </summary>
    public decimal? EffortPoints { get; init; }

    /// <summary>
    /// Gets or sets whether the ticket is visible to external users.
    /// </summary>
    public bool IsVisibleToExternals { get; init; }

    /// <summary>
    /// Gets or sets the status identifier.
    /// </summary>
    public Guid TicketStatusId { get; init; }

    /// <summary>
    /// Gets or sets the complexity identifier.
    /// </summary>
    public Guid TicketComplexityId { get; init; }

    /// <summary>
    /// Gets or sets the time unit identifier.
    /// </summary>
    public Guid TimeUnitId { get; init; }

    /// <summary>
    /// Gets or sets the optional customer identifier.
    /// </summary>
    public Guid? CustomerId { get; init; }

    /// <summary>
    /// Gets or sets the optional project identifier.
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// Gets or sets the optional area identifier.
    /// </summary>
    public Guid? AreaId { get; init; }

    /// <summary>
    /// Gets or sets the channel identifier.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// Gets or sets the optional assigned user identifier.
    /// </summary>
    public Guid? AssignedToUserId { get; init; }

    /// <summary>
    /// Gets or sets the optional precedent ticket identifier.
    /// </summary>
    public Guid? PrecedentTicketId { get; init; }
}
