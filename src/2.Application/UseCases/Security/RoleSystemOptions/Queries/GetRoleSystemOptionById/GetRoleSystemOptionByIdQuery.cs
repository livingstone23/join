using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;

/// <summary>
/// Query used to retrieve a single role-system-option permission rule by identifier.
/// </summary>
public sealed record GetRoleSystemOptionByIdQuery(Guid Id) : IRequest<Response<RoleSystemOptionDto>>;
