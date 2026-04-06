using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.UserCompanies.Commands.SetDefaultCompany;

/// <summary>
/// Requests the change of the default company for a specific user.
/// </summary>
public sealed record SetDefaultCompanyCommand(Guid UserId, Guid CompanyId)
    : IRequest<Response<Guid>>;
