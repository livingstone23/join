using System;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// WebApi-facing DTO for updating an existing <see cref="TicketUserCompanyDto"/>.
/// The user-to-row link cannot be reassigned — to change the user, delete the row and create a new one.
/// The <c>CompanyId</c> is intentionally omitted — it always comes from the authenticated token.
/// </summary>
public record UpdateTicketUserCompanyDto
{
    /// <summary>
    /// Gets whether to grant authority to redistribute tickets.
    /// </summary>
    public bool IsSuperAdminTicket { get; init; }

    /// <summary>
    /// Gets whether to grant authority to finish tickets.
    /// </summary>
    public bool CanFinishTicket { get; init; }

    /// <summary>
    /// Gets whether the user is eligible to receive ticket assignments.
    /// </summary>
    public bool CanResolveTicket { get; init; }
}
