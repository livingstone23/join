using JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;
using JOIN.Domain.Messaging;

namespace JOIN.Application.Mappings;

/// <summary>
/// Define las operaciones Mapperly para mapear entre los commands de
/// configuración de adjuntos y la entidad <see cref="TicketAttachmentSettings"/>.
/// </summary>
public interface ITicketAttachmentSettingsMapper
{
    /// <summary>
    /// Mapea un command de creación a una nueva instancia de la entidad.
    /// </summary>
    TicketAttachmentSettings ToEntity(CreateTicketAttachmentSettingsCommand command);

    /// <summary>
    /// Aplica los valores de un command de actualización sobre la entidad
    /// trackeada, conservando los campos de infraestructura.
    /// </summary>
    void ApplyUpdate(UpdateTicketAttachmentSettingsCommand command, TicketAttachmentSettings entity);
}