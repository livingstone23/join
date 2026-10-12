namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// Global activity type of the calendar catalog (SPEC 44, <c>Calendar.Activities</c>).
/// </summary>
public record CalendarActivityDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Color { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public int DefaultDurationMinutes { get; init; }
    public bool AllowsOverlap { get; init; }
    public bool RequiresBusinessHours { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public DateTime CreatedAt { get; init; }
}
