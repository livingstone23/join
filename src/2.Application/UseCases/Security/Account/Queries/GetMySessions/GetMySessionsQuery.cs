using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using MediatR;



namespace JOIN.Application.UseCases.Security.Account.Queries.GetMySessions;



/// <summary>
/// Represents the query used to retrieve active authenticated sessions for the current user.
/// </summary>
/// <param name="UserId">The unique identifier of the authenticated user extracted from claims.</param>
public sealed record GetMySessionsQuery(Guid UserId) : IRequest<Response<IReadOnlyCollection<ActiveSessionDto>>>;
