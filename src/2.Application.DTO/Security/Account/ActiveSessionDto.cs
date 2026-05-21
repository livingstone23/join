namespace JOIN.Application.DTO.Security.Account;



/// <summary>
/// Represents one active authenticated session associated with the current user.
/// </summary>
public sealed record ActiveSessionDto
{
    /// <summary>
    /// Gets the unique identifier of the session entry.
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the session started.
    /// </summary>
    public DateTime ConnectedAtUtc { get; init; }

    /// <summary>
    /// Gets the UTC timestamp of the last observed activity for the session.
    /// </summary>
    public DateTime LastActivityAtUtc { get; init; }

    /// <summary>
    /// Gets the device or client descriptor registered for the session.
    /// </summary>
    public string? Device { get; init; }

    /// <summary>
    /// Gets the originating IP address registered for the session.
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// Gets a value indicating whether this session corresponds to the caller's current token context.
    /// </summary>
    public bool IsCurrent { get; init; }
}