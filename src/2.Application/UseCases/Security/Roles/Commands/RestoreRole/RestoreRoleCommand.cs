using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Security.Roles.Commands.RestoreRole;

/// <summary>
/// Restores a logically deleted role together with the permissions and company links deleted in the
/// same cascade (SPEC 41, Etapa 3). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="Id">The identifier of the role to restore.</param>
public sealed record RestoreRoleCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;
