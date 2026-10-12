using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Commands.DeleteUserCalendar;

/// <summary>
/// Logically deletes a calendar of the current user.
/// </summary>
public record DeleteUserCalendarCommand(Guid Id) : ITransactionalCommand<Response<bool>>;
