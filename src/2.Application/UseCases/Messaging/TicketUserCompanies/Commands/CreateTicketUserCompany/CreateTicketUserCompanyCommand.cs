using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.CreateTicketUserCompany;

/// <summary>
/// Adds a user to the current tenant's ticket-management roster with the specified capabilities.
/// </summary>
public record CreateTicketUserCompanyCommand : ITransactionalCommand<Response<TicketUserCompanyDto>>
{
    /// <summary>
    /// Gets the user to add to the roster.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets whether the user is granted authority to redistribute tickets.
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
