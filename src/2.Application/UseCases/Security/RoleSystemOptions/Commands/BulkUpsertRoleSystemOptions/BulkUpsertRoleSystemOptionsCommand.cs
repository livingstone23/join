using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands.BulkUpsertRoleSystemOptions;

/// <summary>
/// Replaces the entire set of <c>RoleSystemOption</c> rows for a given role in the
/// caller's tenant in a single SQL transaction. Tenant is always derived from the
/// caller's JWT (SPEC 23), never from the request body.
/// </summary>
/// <param name="RoleId">Target role id. Must exist and be active (GcRecord = 0) in the caller's tenant.</param>
/// <param name="Items">Desired final set of permissions. Capped at 500 items (validator). Duplicates rejected.</param>
public sealed record BulkUpsertRoleSystemOptionsCommand(
    Guid RoleId,
    IReadOnlyList<UpsertRoleSystemOptionItemDto> Items)
    : IRequest<Response<BulkUpsertRoleSystemOptionsResult>>;