using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;

/// <summary>
/// Restores a logically deleted identification type (SPEC 41). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the identification type to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestoreIdentificationTypeCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
