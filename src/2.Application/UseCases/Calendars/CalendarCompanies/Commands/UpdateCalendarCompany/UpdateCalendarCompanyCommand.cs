using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.UpdateCalendarCompany;

/// <summary>
/// Updates the calendar configuration of the current company. Without
/// <see cref="RescheduledByOthersStatusCompanyId"/> the current reschedule status is kept.
/// </summary>
public record UpdateCalendarCompanyCommand : ITransactionalCommand<Response<CalendarCompanyDto>>
{
    public Guid DefaultActivityUserId { get; init; }
    public string TimeZone { get; init; } = string.Empty;
    public bool IsOpen24Hours { get; init; }
    public TimeOnly? BusinessHoursStart { get; init; }
    public TimeOnly? BusinessHoursEnd { get; init; }
    public string? DefaultCalendarName { get; init; }
    public Guid? RescheduledByOthersStatusCompanyId { get; init; }
}
