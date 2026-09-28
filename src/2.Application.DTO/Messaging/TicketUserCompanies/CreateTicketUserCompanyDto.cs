using System;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// WebApi-facing DTO for creating a new <see cref="TicketUserCompanyDto"/>.
/// The <c>CompanyId</c> is intentionally omitted — it always comes from the authenticated token.
/// </summary>
public record CreateTicketUserCompanyDto
{
    /// <summary>
    /// Gets the user to add to the tenant's ticket roster.
    /// </summary>
    public Guid UserId { get; init; }

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
