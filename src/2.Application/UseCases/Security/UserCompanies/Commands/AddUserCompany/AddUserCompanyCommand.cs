using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.UserCompanies.Commands.AddUserCompany;

/// <summary>
/// Adds (or refreshes) a <c>UserCompany</c> membership and assigns the supplied roles
/// to that user inside the target tenant. Introduced by SPEC 28 / F3 for SuperAdmin
/// membership administration. Idempotent: when the membership already exists the
/// <c>UserRoleCompany</c> set is replaced; soft-deleted memberships are reactivated
/// in place to satisfy the unique <c>(UserId, CompanyId)</c> index.
/// </summary>
/// <param name="UserId">User to assign the membership to.</param>
/// <param name="CompanyId">Tenant of the membership.</param>
/// <param name="RoleIds">Roles to grant inside <paramref name="CompanyId"/>.</param>
public sealed record AddUserCompanyCommand(
    Guid UserId,
    Guid CompanyId,
    IReadOnlyList<Guid> RoleIds)
    : ITransactionalCommand<Response<AddUserCompanyResultDto>>;