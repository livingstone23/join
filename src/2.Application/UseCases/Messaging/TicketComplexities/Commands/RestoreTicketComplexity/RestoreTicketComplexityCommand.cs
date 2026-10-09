using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Messaging.TicketComplexities.Commands;

/// <summary>
/// Restores a logically deleted ticket complexity (SPEC 41). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the ticket complexity to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreTicketComplexityCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
