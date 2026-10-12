namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// A calendar of a user. <see cref="IsDefault"/> marks the one referenced by the user's configuration.
/// </summary>
public record UserCalendarDto
{
    public Guid Id { get; init; }
    public Guid OwnerUserId { get; init; }
    public string? OwnerName { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Color { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public bool IsDefault { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Response of <c>GET /UserCalendars/mine</c>: empty with <see cref="PendingProvisioning"/> when the user has no
/// calendar in the company yet.
/// </summary>
public record MyCalendarsDto
{
    public bool PendingProvisioning { get; init; }
    public IReadOnlyList<UserCalendarDto> Calendars { get; init; } = [];
}
