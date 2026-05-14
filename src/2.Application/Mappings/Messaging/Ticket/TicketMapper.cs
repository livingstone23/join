using JOIN.Application.UseCases.Messaging.Tickets.Commands;
using JOIN.Domain.Messaging;
using Riok.Mapperly.Abstractions;



namespace JOIN.Application.Mappings;



/// <summary>
/// Auto-generated mapper for ticket commands and entities using Mapperly.
/// </summary>
[Mapper]
public partial class TicketMapper : ITicketMapper
{

    
    /// <summary>
    /// Maps a create command to a ticket entity while ignoring infrastructure-owned fields.
    /// </summary>
    [MapperIgnoreTarget(nameof(Ticket.Id))]
    [MapperIgnoreTarget(nameof(Ticket.CompanyId))]
    [MapperIgnoreTarget(nameof(Ticket.Code))]
    [MapperIgnoreTarget(nameof(Ticket.CreatedByUserId))]
    [MapperIgnoreTarget(nameof(Ticket.Created))]
    [MapperIgnoreTarget(nameof(Ticket.CreatedBy))]
    [MapperIgnoreTarget(nameof(Ticket.LastModified))]
    [MapperIgnoreTarget(nameof(Ticket.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(Ticket.GcRecord))]
    [MapperIgnoreTarget(nameof(Ticket.Company))]
    [MapperIgnoreTarget(nameof(Ticket.Person))]
    [MapperIgnoreTarget(nameof(Ticket.Project))]
    [MapperIgnoreTarget(nameof(Ticket.Area))]
    [MapperIgnoreTarget(nameof(Ticket.Channel))]
    [MapperIgnoreTarget(nameof(Ticket.CreatedByUser))]
    [MapperIgnoreTarget(nameof(Ticket.AssignedToUser))]
    [MapperIgnoreTarget(nameof(Ticket.Status))]
    [MapperIgnoreTarget(nameof(Ticket.Complexity))]
    [MapperIgnoreTarget(nameof(Ticket.TimeUnit))]
    [MapperIgnoreTarget(nameof(Ticket.PrecedentTicket))]
    [MapperIgnoreTarget(nameof(Ticket.Notifications))]
    [MapperIgnoreTarget(nameof(Ticket.ChildTickets))]
    public partial Ticket ToEntity(CreateTicketCommand command);

    /// <summary>
    /// Applies updates from command into an existing tracked ticket entity.
    /// </summary>
    [MapperIgnoreTarget(nameof(Ticket.Id))]
    [MapperIgnoreTarget(nameof(Ticket.CompanyId))]
    [MapperIgnoreTarget(nameof(Ticket.Code))]
    [MapperIgnoreTarget(nameof(Ticket.CreatedByUserId))]
    [MapperIgnoreTarget(nameof(Ticket.Created))]
    [MapperIgnoreTarget(nameof(Ticket.CreatedBy))]
    [MapperIgnoreTarget(nameof(Ticket.LastModified))]
    [MapperIgnoreTarget(nameof(Ticket.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(Ticket.GcRecord))]
    [MapperIgnoreTarget(nameof(Ticket.Company))]
    [MapperIgnoreTarget(nameof(Ticket.Person))]
    [MapperIgnoreTarget(nameof(Ticket.Project))]
    [MapperIgnoreTarget(nameof(Ticket.Area))]
    [MapperIgnoreTarget(nameof(Ticket.Channel))]
    [MapperIgnoreTarget(nameof(Ticket.CreatedByUser))]
    [MapperIgnoreTarget(nameof(Ticket.AssignedToUser))]
    [MapperIgnoreTarget(nameof(Ticket.Status))]
    [MapperIgnoreTarget(nameof(Ticket.Complexity))]
    [MapperIgnoreTarget(nameof(Ticket.TimeUnit))]
    [MapperIgnoreTarget(nameof(Ticket.PrecedentTicket))]
    [MapperIgnoreTarget(nameof(Ticket.Notifications))]
    [MapperIgnoreTarget(nameof(Ticket.ChildTickets))]
    [MapperIgnoreSource(nameof(UpdateTicketCommand.Id))]
    public partial void ApplyUpdate(UpdateTicketCommand command, Ticket ticket);


}
