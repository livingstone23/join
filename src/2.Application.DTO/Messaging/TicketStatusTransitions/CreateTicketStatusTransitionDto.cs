using System;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// WebApi-facing DTO for creating a new <see cref="TicketStatusTransitionDto"/>.
/// The tenant comes from the authenticated token — it is intentionally omitted.
/// </summary>
public record CreateTicketStatusTransitionDto
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