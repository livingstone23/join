using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Restores a logically deleted company module (SPEC 41). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the company module to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreCompanyModuleCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
