using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.RestoreTicketDocument;

/// <summary>
/// Restores a logically deleted ticket attachment (SPEC 41, Etapa 4). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="TicketId">The ticket the attachment belongs to (route).</param>
/// <param name="Id">The identifier of the attachment to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreTicketDocumentCommand(Guid TicketId, Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
