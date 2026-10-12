namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// Calendar configuration of a user in the company, with the effective business hours and the company data the
/// frontend needs. <see cref="HasCalendar"/> is <c>false</c> for users still pending provisioning.
/// </summary>
public record CalendarUserConfigurationDto
{
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public bool HasCalendar { get; init; }
    public Guid? Id { get; init; }
    public Guid? DefaultCalendarId { get; init; }
    public string? DefaultCalendarName { get; init; }
    public TimeOnly? BusinessHoursStart { get; init; }
    public TimeOnly? BusinessHoursEnd { get; init; }

    /// <summary> <c>CalendarWorkDays</c> flags (Sunday = 1 … Saturday = 64). </summary>
    public int WorkDays { get; init; }

    /// <summary> <c>CalendarViewType</c>: Day = 1, Week = 2, Month = 3, Agenda = 4. </summary>
    public int DefaultView { get; init; }

    /// <summary> <c>DayOfWeek</c>: Sunday = 0, Monday = 1. </summary>
    public int WeekStartsOn { get; init; }

    public TimeOnly? EffectiveBusinessHoursStart { get; init; }

    /// <summary> <c>null</c> with a start of 00:00 means the whole day. </summary>
    public TimeOnly? EffectiveBusinessHoursEnd { get; init; }

    public string? TimeZone { get; init; }
    public bool CompanyIsOpen24Hours { get; init; }
    public TimeOnly? CompanyBusinessHoursStart { get; init; }
    public TimeOnly? CompanyBusinessHoursEnd { get; init; }
}
