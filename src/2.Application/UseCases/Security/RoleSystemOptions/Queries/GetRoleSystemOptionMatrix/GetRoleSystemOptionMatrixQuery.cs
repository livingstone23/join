using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Queries.GetRoleSystemOptionMatrix;

/// <summary>
/// Query for the full permissions matrix of a role in the caller's tenant.
/// Returns every active <c>SystemOption</c> grouped by <c>SystemModule</c>,
/// with both the option's <c>Supports</c> defaults and the role's <c>Granted</c> flags.
/// </summary>
/// <param name="RoleId">Target role id. Must exist and be active (GcRecord = 0).</param>
public sealed record GetRoleSystemOptionMatrixQuery(Guid RoleId)
    : IRequest<Response<RoleSystemOptionMatrixDto>>;