using JOIN.Application.UseCases.Messaging.Tickets.Commands;
using JOIN.Domain.Messaging;

namespace JOIN.Application.Mappings;

/// <summary>
/// Defines mapping operations for ticket commands and entities.
/// </summary>
public interface ITicketMapper
{
    /// <summary>
    /// Maps a create command into a ticket entity.
    /// </summary>
    Ticket ToEntity(CreateTicketCommand command);

    /// <summary>
    /// Applies scalar updates from an update command into an existing ticket entity.
    /// </summary>
    void ApplyUpdate(UpdateTicketCommand command, Ticket ticket);
}
