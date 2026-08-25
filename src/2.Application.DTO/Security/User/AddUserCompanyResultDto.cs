namespace JOIN.Application.DTO.Security;

/// <summary>
/// Response payload for <c>POST /Users/{userId}/companies</c>. Mirrors the freshly
/// written membership: <c>IsDefault</c> reflects the persisted state (true only when
/// the user had no other active company), <c>RoleIdsAssigned</c> the set that is now
/// active in <c>UserRoleCompanies</c> for this (user, company) pair.
/// </summary>
/// <param name="UserId">User whose membership was added or refreshed.</param>
/// <param name="CompanyId">Tenant the membership was added or refreshed in.</param>
/// <param name="IsDefault">Whether this membership is now the user's default company.</param>
/// <param name="RoleIdsAssigned">Roles that are now active for the user inside the tenant.</param>
public sealed record AddUserCompanyResultDto(
    Guid UserId,
    Guid CompanyId,
    bool IsDefault,
    IReadOnlyList<Guid> RoleIdsAssigned);