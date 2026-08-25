namespace JOIN.Application.DTO.Security;

/// <summary>
/// One user's outcome inside a <c>PUT /Users/roles/bulk</c> batch. SPEC 28 / F6.
/// <c>RolesAdded</c> / <c>RolesRemoved</c> count what was actually written, not what
/// was requested — the latter is invisible to the caller when the user already had
/// the resulting set or the role was not in the tenant.
/// </summary>
/// <param name="UserId">User whose outcome is being reported.</param>
/// <param name="Outcome">What happened to this user's roles.</param>
/// <param name="RolesAdded">Count of roles that were inserted or reactivated.</param>
/// <param name="RolesRemoved">Count of roles that were soft-deleted.</param>
public sealed record BulkUpdateUserRoleItemDto(
    Guid UserId,
    BulkRoleOutcome Outcome,
    int RolesAdded,
    int RolesRemoved);

/// <summary>
/// Aggregate result of a <c>PUT /Users/roles/bulk</c> request. One item per user in
/// the request (preserving the request order) plus two aggregate counters so the UI
/// can render "N updated, M skipped" without iterating the array.
/// </summary>
/// <param name="Items">Per-user outcomes, same order as the request's <c>UserIds</c>.</param>
/// <param name="UsersUpdated">Count of <c>Outcome == Updated</c> items.</param>
/// <param name="UsersSkipped">Count of <c>Outcome == UserNotFound</c> items.</param>
public sealed record BulkUpdateUserRolesResultDto(
    IReadOnlyList<BulkUpdateUserRoleItemDto> Items,
    int UsersUpdated,
    int UsersSkipped);