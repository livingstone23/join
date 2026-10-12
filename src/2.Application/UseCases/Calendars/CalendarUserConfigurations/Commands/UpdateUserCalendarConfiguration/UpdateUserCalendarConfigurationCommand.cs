using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateUserCalendarConfiguration;

/// <summary>
/// Updates the calendar configuration of a user of the company (contract hours, work days and preferences).
/// Hours: both or neither (neither = the company's), inside the company hours.
/// </summary>
public record UpdateUserCalendarConfigurationCommand : ITransactionalCommand<Response<CalendarUserConfigurationDto>>
{
    [JsonIgnore]
    public Guid UserId { get; init; }

    public TimeOnly? BusinessHoursStart { get; init; }
    public TimeOnly? BusinessHoursEnd { get; init; }
    public CalendarWorkDays WorkDays { get; init; } = CalendarWorkDays.Weekdays;
    public CalendarViewType DefaultView { get; init; } = CalendarViewType.Week;
    public DayOfWeek WeekStartsOn { get; init; } = DayOfWeek.Monday;
}
