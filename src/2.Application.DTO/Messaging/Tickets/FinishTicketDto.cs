using System;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// WebApi-facing DTO for finalizing a ticket via <c>PUT /api/v1/Tickets/{id}/finish</c>.
/// The actor must hold either the <c>CanFinishTicket</c> or the <c>IsSuperAdminTicket</c>
/// capability; the target status must have <c>IsFinal = true</c>.
/// </summary>
public record FinishTicketDto
{
    /// <summary>
    /// Gets the final status the ticket should transition to.
    /// </summary>
    public Guid TicketStatusId { get; init; }

    /// <summary>
    /// Gets the optional resolution summary recorded as the finalization log summary.
    /// </summary>
    public string? ResolutionSummary { get; init; }
}
