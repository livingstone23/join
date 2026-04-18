using JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;
using JOIN.Domain.Messaging;

namespace JOIN.Application.Mappings;

/// <summary>
/// Defines Mapperly operations for tenant ticket default configuration entities.
/// </summary>
public interface ITicketCompanyDefaultMapper
{
    /// <summary>
    /// Maps a creation command to a new entity instance.
    /// </summary>
    TicketCompanyDefault ToEntity(CreateTicketCompanyDefaultCommand command);

    /// <summary>
    /// Applies update values from a command into a tracked entity.
    /// </summary>
    void ApplyUpdate(UpdateTicketCompanyDefaultCommand command, TicketCompanyDefault entity);
}
