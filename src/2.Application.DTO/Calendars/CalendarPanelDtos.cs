namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// A user of the company for the panel filter.
/// </summary>
public record CalendarPanelUserDto
{
    public Guid UserId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Email { get; init; }
    public bool HasCalendar { get; init; }
}

/// <summary>
/// Free slots of a user (<c>GET /CalendarPanel/availability</c>), in blocks of the activity type duration.
/// </summary>
public record CalendarAvailabilityDto
{
    public Guid UserId { get; init; }
    public Guid ActivityCompanyId { get; init; }
    public int DurationMinutes { get; init; }
    public string TimeZone { get; init; } = string.Empty;
    public IReadOnlyList<CalendarSlotDto> Slots { get; init; } = [];
}

/// <summary>
/// A free slot in UTC.
/// </summary>
public record CalendarSlotDto(DateTime StartUtc, DateTime EndUtc);
