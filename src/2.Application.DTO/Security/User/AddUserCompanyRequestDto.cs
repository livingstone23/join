namespace JOIN.Application.DTO.Security;

/// <summary>
/// Request payload for <c>POST /Users/{userId}/companies</c>. Introduced by SPEC 28
/// item 18 to give SuperAdmin a way to add (or replace the role set of) a user's
/// membership in any tenant. The <c>CompanyId</c> in the body is the tenant the new
/// membership lives in; this is not required to match the caller's tenant context
/// because the endpoint is SuperAdmin-only.
/// </summary>
/// <param name="CompanyId">Tenant of the membership to create or refresh.</param>
/// <param name="RoleIds">Roles to assign inside that tenant. Replaces the existing set when the membership is already active.</param>
public sealed record AddUserCompanyRequestDto(
    Guid CompanyId,
    IReadOnlyList<Guid> RoleIds);