namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// Activity type enabled in the company with its effective values, plus the global values to compare
/// (<c>GlobalName</c>, <c>GlobalIsActive</c>). <see cref="IsUsable"/> applies the "usable" rule of SPEC 44.
/// </summary>
public record CalendarActivityCompanyDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid CalendarActivityId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string GlobalName { get; init; } = string.Empty;
    public bool GlobalIsActive { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public int DefaultDurationMinutes { get; init; }
    public bool AllowsOverlap { get; init; }
    public bool RequiresBusinessHours { get; init; }
    public Guid InitialCalendarStatusCompanyId { get; init; }
    public string? InitialStatusName { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public bool IsUsable => IsActive && GlobalIsActive;
    public DateTime CreatedAt { get; init; }
}
