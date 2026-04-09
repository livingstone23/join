using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;

/// <summary>
/// Command to register a new ticket status in the messaging catalog.
/// </summary>
public record CreateTicketStatusCommand : IRequest<Response<TicketStatusDto>>
{
    /// <summary>
    /// Gets or sets the display name of the ticket status.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional description of the ticket status.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets the numeric code associated with the ticket status.
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// Gets or sets whether the ticket status is active.
    /// </summary>
    public bool IsActive { get; init; } = true;
}
