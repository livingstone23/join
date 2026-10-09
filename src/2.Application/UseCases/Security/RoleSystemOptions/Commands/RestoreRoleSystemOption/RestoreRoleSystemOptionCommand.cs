using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Restores a logically deleted role permission (SPEC 41). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the role permission to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreRoleSystemOptionCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
