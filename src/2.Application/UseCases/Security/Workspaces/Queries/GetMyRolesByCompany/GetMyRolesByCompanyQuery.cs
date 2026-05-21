using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Workspaces;
using MediatR;



namespace JOIN.Application.UseCases.Security.Workspaces.Queries.GetMyRolesByCompany;



/// <summary>
/// Represents the query used to retrieve the current user's role assignments in a specific company context.
/// </summary>
/// <param name="UserId">The unique identifier of the authenticated user extracted from claims.</param>
/// <param name="CompanyId">The company identifier used to scope role assignments.</param>
public sealed record GetMyRolesByCompanyQuery(Guid UserId, Guid CompanyId) : IRequest<Response<IReadOnlyCollection<MyCompanyRoleDto>>>;