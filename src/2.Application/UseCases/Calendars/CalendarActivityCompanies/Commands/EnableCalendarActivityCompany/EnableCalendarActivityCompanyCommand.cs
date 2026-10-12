using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.EnableCalendarActivityCompany;

/// <summary>
/// Enables a global activity type in the current company, copying its values. Without an initial status the
/// company's <c>PENDING</c> status is used.
/// </summary>
public record EnableCalendarActivityCompanyCommand : ITransactionalCommand<Response<CalendarActivityCompanyDto>>
{
    public Guid CalendarActivityId { get; init; }
    public Guid? InitialCalendarStatusCompanyId { get; init; }
}
