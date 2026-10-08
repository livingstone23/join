using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Commands;

/// <summary>
/// Restores a logically deleted income range (SPEC 41). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the income range to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreIncomeRangeCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
