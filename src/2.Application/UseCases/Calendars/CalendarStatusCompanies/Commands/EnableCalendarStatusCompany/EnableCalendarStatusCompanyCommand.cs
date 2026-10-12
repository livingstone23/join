using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.EnableCalendarStatusCompany;

/// <summary>
/// Enables a global status in the current company, copying its name, color and order.
/// </summary>
public record EnableCalendarStatusCompanyCommand : ITransactionalCommand<Response<CalendarStatusCompanyDto>>
{
    public Guid CalendarStatusId { get; init; }
}
