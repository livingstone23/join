using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Workspaces;
using MediatR;



namespace JOIN.Application.UseCases.Security.Workspaces.Queries.GetMyCompanies;



/// <summary>
/// Represents the query used to retrieve companies assigned to the current authenticated user.
/// </summary>
/// <param name="UserId">The unique identifier of the authenticated user extracted from claims.</param>
public sealed record GetMyCompaniesQuery(Guid UserId) : IRequest<Response<IReadOnlyCollection<MyCompanyItemDto>>>;