using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Admin.Projects.Commands;

/// <summary>
/// Restores a logically deleted project (SPEC 41). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the project to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreProjectCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
