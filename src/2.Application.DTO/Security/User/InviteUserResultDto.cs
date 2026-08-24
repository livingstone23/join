namespace JOIN.Application.DTO.Security.User;

/// <summary>
/// Outcome of an invitation attempt. Distinguishes new users, multi-tenant additions, and
/// re-sends so the UI can adapt the confirmation copy.
/// </summary>
public sealed record InviteUserResultDto(
    Guid UserId,
    string Email,
    InviteOutcome Outcome);
