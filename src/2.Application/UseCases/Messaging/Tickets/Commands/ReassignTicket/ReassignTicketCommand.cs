using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.ReassignTicket;

/// <summary>
/// Assigns or reassigns a ticket to a different user. Single command covering both the
/// first assignment of a previously-unassigned ticket and the reassignment of one
/// already assigned. Gated by the actor's <c>IsSuperAdminTicket</c> capability.
/// </summary>
public record ReassignTicketCommand : ITransactionalCommand<Response<TicketDto>>
{
    /// <summary>
    /// Gets the ticket identifier to reassign.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; init; }

    /// <summary>
    /// Gets the user the ticket should be assigned to.
    /// </summary>
    public Guid NewAssignedToUserId { get; init; }
}
