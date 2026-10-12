namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// Company holiday. <see cref="OccursOn"/> is the date in the requested year (equal to <see cref="Date"/> for a
/// one-time holiday; same day and month for a recurring one).
/// </summary>
public record CalendarCompanyNonWorkingDayDto
{
    public Guid Id { get; init; }
    public DateOnly Date { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsRecurringYearly { get; init; }
    public DateOnly OccursOn { get; init; }
    public DateTime CreatedAt { get; init; }
}
