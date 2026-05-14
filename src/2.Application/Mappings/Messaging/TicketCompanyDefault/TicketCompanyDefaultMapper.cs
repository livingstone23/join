using JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;
using JOIN.Domain.Messaging;
using Riok.Mapperly.Abstractions;



namespace JOIN.Application.Mappings;



/// <summary>
/// Auto-generated mapper for tenant ticket default configuration entities.
/// </summary>
[Mapper]
public partial class TicketCompanyDefaultMapper : ITicketCompanyDefaultMapper
{

    /// <summary>
    /// Maps a create command into a new entity while ignoring infrastructure-owned fields.
    /// </summary>
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.Id))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.CompanyId))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.Created))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.CreatedBy))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.LastModified))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.GcRecord))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.Company))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.TicketStatusDefault))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.TicketComplexityDefault))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.TimeUnitDefault))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.AreaDefault))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.ProjectDefault))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.ChannelDefault))]
    public partial TicketCompanyDefault ToEntity(CreateTicketCompanyDefaultCommand command);

    /// <summary>
    /// Applies update values from a command into an existing tracked entity.
    /// </summary>
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.Id))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.CompanyId))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.Created))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.CreatedBy))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.LastModified))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.GcRecord))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.Company))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.TicketStatusDefault))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.TicketComplexityDefault))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.TimeUnitDefault))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.AreaDefault))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.ProjectDefault))]
    [MapperIgnoreTarget(nameof(TicketCompanyDefault.ChannelDefault))]
    [MapperIgnoreSource(nameof(UpdateTicketCompanyDefaultCommand.Id))]
    public partial void ApplyUpdate(UpdateTicketCompanyDefaultCommand command, TicketCompanyDefault entity);

}


