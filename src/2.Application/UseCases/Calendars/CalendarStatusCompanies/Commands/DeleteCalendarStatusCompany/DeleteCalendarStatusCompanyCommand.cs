using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.DeleteCalendarStatusCompany;

/// <summary>
/// Removes a status from the company, with the blocks of SPEC 44 (data model 4).
/// </summary>
public record DeleteCalendarStatusCompanyCommand(Guid Id) : ITransactionalCommand<Response<bool>>;
