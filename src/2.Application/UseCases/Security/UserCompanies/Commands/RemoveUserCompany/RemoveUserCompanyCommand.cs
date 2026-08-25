using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.UserCompanies.Commands.RemoveUserCompany;

/// <summary>
/// Removes a <c>UserCompany</c> membership and every <c>UserRoleCompany</c> tied to
/// that (user, company) pair. Introduced by SPEC 28 / F4, item 18. Guard order
/// matters: last-company check runs before default-company so a single-tenant user
/// gets the more useful error code.
/// </summary>
/// <param name="UserId">User whose membership should be removed.</param>
/// <param name="CompanyId">Tenant whose membership should be removed.</param>
public sealed record RemoveUserCompanyCommand(Guid UserId, Guid CompanyId)
    : ITransactionalCommand<Response<bool>>;