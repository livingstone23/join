using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Common.Countries.Commands;

/// <summary>
/// Restores a logically deleted country (SPEC 41). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the country to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreCountryCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
