using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.UserCompanies.Queries.GetUserCompanies;

/// <summary>
/// Represents the query used to retrieve all active company assignments associated with a specific user account.
/// This request is especially useful for user-context switching screens and security administration workflows that need to show the user's available company scope.
/// </summary>
/// <param name="UserId">The unique identifier of the user whose company assignments should be listed.</param>
public sealed record GetUserCompaniesQuery(Guid UserId)
    : IRequest<Response<IEnumerable<UserCompanyDto>>>;
