using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.UserCompanies.Commands.SetDefaultCompany;

/// <summary>
/// Represents the command used to change the default company assignment for a specific user.
/// The operation ensures that one company becomes the active operational context while any previous default assignment is cleared.
/// </summary>
/// <param name="UserId">The unique identifier of the user whose default company should be updated.</param>
/// <param name="CompanyId">The unique identifier of the company that must become the new default context.</param>
public sealed record SetDefaultCompanyCommand(Guid UserId, Guid CompanyId)
    : IRequest<Response<Guid>>;
