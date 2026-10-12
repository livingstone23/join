namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// Global status of the calendar catalog (SPEC 44, <c>Calendar.Statuses</c>).
/// </summary>
public record CalendarStatusDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Color { get; init; } = string.Empty;
    public bool BlocksTime { get; init; }
    public bool IsFinal { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public DateTime CreatedAt { get; init; }
}
