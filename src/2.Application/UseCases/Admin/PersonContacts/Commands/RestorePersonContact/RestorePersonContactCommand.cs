using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Admin.PersonContacts.Commands;

/// <summary>
/// Restores a logically deleted person contact (SPEC 41). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the person contact to restore.</param>
/// <param name="CompanyId">Optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).</param>
public sealed record RestorePersonContactCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
