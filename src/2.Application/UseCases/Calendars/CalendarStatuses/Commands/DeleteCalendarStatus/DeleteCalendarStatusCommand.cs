using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.DeleteCalendarStatus;

/// <summary>
/// Logically deletes a global status. System statuses and statuses enabled in a company cannot be deleted.
/// </summary>
public record DeleteCalendarStatusCommand(Guid Id) : ITransactionalCommand<Response<bool>>;
