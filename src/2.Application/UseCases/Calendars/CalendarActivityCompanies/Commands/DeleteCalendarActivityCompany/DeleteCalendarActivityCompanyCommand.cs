using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.DeleteCalendarActivityCompany;

/// <summary>
/// Removes an activity type from the company. When activities of the type exist, it is deactivated instead
/// (<c>Data = false</c>) so they keep their type.
/// </summary>
public record DeleteCalendarActivityCompanyCommand(Guid Id) : ITransactionalCommand<Response<bool>>;
