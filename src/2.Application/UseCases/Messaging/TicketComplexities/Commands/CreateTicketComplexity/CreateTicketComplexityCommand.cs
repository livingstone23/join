using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketComplexities.Commands;

/// <summary>
/// Command to register a new ticket complexity in the messaging catalog.
/// </summary>
public record CreateTicketComplexityCommand : IRequest<Response<TicketComplexityDto>>
{
    /// <summary>
    /// Gets or sets the display name of the ticket complexity.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional description of the ticket complexity.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets the numeric code associated with the ticket complexity.
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// Gets or sets the amount of configured time units required to resolve a ticket with this complexity.
    /// </summary>
    public int ResolutionTimeUnits { get; init; }

    /// <summary>
    /// Gets or sets the related time unit identifier.
    /// </summary>
    public Guid TimeUnitId { get; init; }

    /// <summary>
    /// Gets or sets whether the ticket complexity is active.
    /// </summary>
    public bool IsActive { get; init; } = true;
}
