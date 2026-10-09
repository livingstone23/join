using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Admin.EntityStatuses.Commands;

/// <summary>
/// Restores a logically deleted entity status (SPEC 41). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the entity status to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreEntityStatusCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
