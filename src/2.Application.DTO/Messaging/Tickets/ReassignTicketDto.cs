using System;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// WebApi-facing DTO for assigning or reassigning a ticket via <c>PUT /api/v1/Tickets/{id}/reassign</c>.
/// The actor must hold the <c>IsSuperAdminTicket</c> capability for the tenant; the target must
/// hold the <c>CanResolveTicket</c> capability.
/// </summary>
public record ReassignTicketDto
{
    /// <summary>
    /// Gets the user the ticket should be assigned to.
    /// </summary>
    public Guid NewAssignedToUserId { get; init; }
}
