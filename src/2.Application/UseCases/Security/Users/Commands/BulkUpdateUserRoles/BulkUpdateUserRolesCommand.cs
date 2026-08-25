using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Users.Commands.BulkUpdateUserRoles;

/// <summary>
/// Applies a delta of role additions and removals to a batch of users inside the
/// caller's tenant. SPEC 28 / F6, item 20. Semantics:
///   * addRoleIds → rows that are missing are inserted or reactivated;
///   * removeRoleIds → active rows are soft-deleted;
///   * untouched roles are left alone.
/// Per-user outcomes are reported in <see cref="BulkUpdateUserRolesResultDto"/>.
/// </summary>
/// <param name="UserIds">Users whose roles will be mutated.</param>
/// <param name="AddRoleIds">Roles to add (or reactivate) on each user.</param>
/// <param name="RemoveRoleIds">Roles to soft-delete on each user.</param>
public sealed record BulkUpdateUserRolesCommand(
    IReadOnlyList<Guid> UserIds,
    IReadOnlyList<Guid> AddRoleIds,
    IReadOnlyList<Guid> RemoveRoleIds)
    : ITransactionalCommand<Response<BulkUpdateUserRolesResultDto>>;