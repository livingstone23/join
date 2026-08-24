namespace JOIN.Application.DTO.Security.User;

/// <summary>
/// Payload for <c>POST /Users/invite</c>. <c>CompanyId</c> is intentionally absent — it
/// always comes from the caller JWT (SPEC 23).
/// </summary>
public sealed record InviteUserRequestDto(
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyList<Guid> RoleIds);
