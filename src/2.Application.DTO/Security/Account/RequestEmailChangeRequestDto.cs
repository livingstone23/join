namespace JOIN.Application.DTO.Security.Account;



/// <summary>
/// Represents the request payload used to start an email change confirmation workflow.
/// </summary>
public sealed record RequestEmailChangeRequestDto
{
    /// <summary>
    /// Gets the new email address that must be confirmed by the user.
    /// </summary>
    public string NewEmail { get; init; } = string.Empty;
}