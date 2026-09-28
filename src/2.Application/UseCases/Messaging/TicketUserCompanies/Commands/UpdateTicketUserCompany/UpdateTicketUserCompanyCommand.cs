using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.UpdateTicketUserCompany;

/// <summary>
/// Updates the capability flags of an existing ticket-roster entry. The user-to-row link
/// cannot be reassigned — delete and recreate the row to change the user.
/// </summary>
public record UpdateTicketUserCompanyCommand : ITransactionalCommand<Response<TicketUserCompanyDto>>
{
    /// <summary>
    /// Gets the row identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets whether to grant authority to redistribute tickets.
    /// </summary>
    public bool IsSuperAdminTicket { get; init; }

    /// <summary>
    /// Gets whether the user can mark a ticket as finished.
    /// </summary>
    public bool CanFinishTicket { get; init; }

    /// <summary>
    /// Gets whether the user is eligible to receive ticket assignments.
    /// </summary>
    public bool CanResolveTicket { get; init; }
}
