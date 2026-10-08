using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Commands.RestoreTicketStatusTransition;

/// <summary>
/// Restores a logically deleted ticket status transition (SPEC 41, Etapa 4). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the ticket status transition to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreTicketStatusTransitionCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
