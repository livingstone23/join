using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands;

/// <summary>
/// Restores a logically deleted ticket together with the attachments of its cascade (SPEC 41, Etapa 4).
/// Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the ticket to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreTicketCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
