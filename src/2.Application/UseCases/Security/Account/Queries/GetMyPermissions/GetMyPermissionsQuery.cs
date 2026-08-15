using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Queries.GetMyPermissions;

/// <summary>
/// Backs <c>GET /api/v1/account/my-permissions</c> (SPEC 26 / F4).
/// Reads the combined CRUD matrix for the caller inside the active tenant.
/// </summary>
public sealed record GetMyPermissionsQuery : IRequest<Response<MyPermissionsDto>>;
