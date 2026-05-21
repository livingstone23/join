namespace JOIN.Application.DTO.Security.Account;



/// <summary>
/// Represents a communication channel associated with the authenticated user's account.
/// </summary>
public sealed record CommunicationChannelDto
{
    /// <summary>
    /// Gets the channel type label (for example, mobile, phone, or messaging app).
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Gets the channel value (for example, phone number or handle).
    /// </summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the channel is marked as preferred.
    /// </summary>
    public bool IsPreferred { get; init; }
}