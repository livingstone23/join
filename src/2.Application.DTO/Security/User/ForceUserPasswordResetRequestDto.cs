namespace JOIN.Application.DTO.Security.User;

/// <summary>
/// Payload for <c>POST /Users/{userId}/force-password-reset</c>. <c>Reason</c> is
/// optional but surfaces inside the email sent to the user (SPEC 27 item 17).
/// </summary>
public sealed record ForceUserPasswordResetRequestDto(string? Reason);
