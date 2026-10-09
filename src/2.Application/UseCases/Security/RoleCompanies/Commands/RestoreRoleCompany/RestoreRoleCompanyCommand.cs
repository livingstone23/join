using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Commands.DeleteRoleCompany;

/// <summary>
/// Restores a logically deleted role company link (SPEC 41). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the role company link to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreRoleCompanyCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
