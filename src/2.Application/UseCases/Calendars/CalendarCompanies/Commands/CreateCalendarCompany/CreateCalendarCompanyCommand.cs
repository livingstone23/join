using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.CreateCalendarCompany;

/// <summary>
/// Creates the calendar configuration of the current company and provisions the calendars of its users.
/// Without <see cref="RescheduledByOthersStatusCompanyId"/> the company's <c>RESCHEDULED</c> status is used.
/// </summary>
public record CreateCalendarCompanyCommand : ITransactionalCommand<Response<CalendarCompanyDto>>
{
    public Guid DefaultActivityUserId { get; init; }
    public string TimeZone { get; init; } = string.Empty;
    public bool IsOpen24Hours { get; init; }
    public TimeOnly? BusinessHoursStart { get; init; }
    public TimeOnly? BusinessHoursEnd { get; init; }
    public string? DefaultCalendarName { get; init; }
    public Guid? RescheduledByOthersStatusCompanyId { get; init; }
}
