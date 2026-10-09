using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Commands;

/// <summary>
/// Restores a logically deleted tax regime (SPEC 41). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the tax regime to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreTaxRegimeCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
