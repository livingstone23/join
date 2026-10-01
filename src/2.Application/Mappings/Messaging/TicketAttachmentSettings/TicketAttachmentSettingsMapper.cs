using JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;
using JOIN.Domain.Messaging;
using Riok.Mapperly.Abstractions;

namespace JOIN.Application.Mappings;

/// <summary>
/// Mapper auto-generado por Mapperly para la entidad
/// <see cref="TicketAttachmentSettings"/>. Sigue el mismo patrón que
/// <see cref="TicketCompanyDefaultMapper"/>: ignora todos los campos de
/// infraestructura (Id, CompanyId, Created/Modified, GcRecord, navigation
/// a Company) y deja que el handler los asigne explícitamente.
/// </summary>
[Mapper]
public partial class TicketAttachmentSettingsMapper : ITicketAttachmentSettingsMapper
{
    /// <summary>
    /// Mapea un command de creación a una nueva entidad, ignorando los
    /// campos de infraestructura.
    /// </summary>
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.Id))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.CompanyId))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.Created))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.CreatedBy))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.LastModified))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.GcRecord))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.Company))]
    public partial TicketAttachmentSettings ToEntity(CreateTicketAttachmentSettingsCommand command);

    /// <summary>
    /// Aplica los valores de un command de actualización sobre la entidad
    /// trackeada, conservando los campos de infraestructura.
    /// </summary>
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.Id))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.CompanyId))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.Created))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.CreatedBy))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.LastModified))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.GcRecord))]
    [MapperIgnoreTarget(nameof(TicketAttachmentSettings.Company))]
    [MapperIgnoreSource(nameof(UpdateTicketAttachmentSettingsCommand.Id))]
    public partial void ApplyUpdate(UpdateTicketAttachmentSettingsCommand command, TicketAttachmentSettings entity);
}