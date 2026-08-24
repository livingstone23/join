namespace JOIN.Application.DTO.Security.User;

/// <summary>
/// Payload for <c>PUT /Users/{userId}/status</c>. <c>Reason</c> is required by the
/// validator so the admin always documents the change (SPEC 27 item 16).
/// </summary>
public sealed record ChangeUserStatusRequestDto(bool IsActive, string Reason);
