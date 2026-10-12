using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.UpdateCalendarCompanyNonWorkingDay;

/// <summary>
/// Updates a company holiday.
/// </summary>
public record UpdateCalendarCompanyNonWorkingDayCommand : ITransactionalCommand<Response<CalendarCompanyNonWorkingDayDto>>
{
    [JsonIgnore]
    public Guid Id { get; init; }

    public DateOnly Date { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsRecurringYearly { get; init; }
}
