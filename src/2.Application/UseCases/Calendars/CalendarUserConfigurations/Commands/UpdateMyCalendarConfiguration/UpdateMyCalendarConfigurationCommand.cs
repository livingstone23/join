using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateMyCalendarConfiguration;

/// <summary>
/// Updates the current user's preferences. Business hours and work days are contract data and are edited only by
/// the administrator (<c>CalendarSettings</c>).
/// </summary>
public record UpdateMyCalendarConfigurationCommand : ITransactionalCommand<Response<CalendarUserConfigurationDto>>
{
    public CalendarViewType DefaultView { get; init; } = CalendarViewType.Week;
    public DayOfWeek WeekStartsOn { get; init; } = DayOfWeek.Monday;
}
