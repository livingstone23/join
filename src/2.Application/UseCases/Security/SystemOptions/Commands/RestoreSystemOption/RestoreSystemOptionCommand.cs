using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Security.SystemOptions.Commands;

/// <summary>
/// Restores a logically deleted system option (SPEC 41). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the system option to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreSystemOptionCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
