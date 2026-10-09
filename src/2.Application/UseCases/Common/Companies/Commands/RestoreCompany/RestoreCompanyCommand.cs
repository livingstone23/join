using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Common.Companies.Commands;

/// <summary>
/// Restores a logically deleted company (SPEC 41). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the company to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreCompanyCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
