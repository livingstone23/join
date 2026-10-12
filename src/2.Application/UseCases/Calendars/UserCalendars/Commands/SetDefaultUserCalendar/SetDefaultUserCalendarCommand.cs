using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Commands.SetDefaultUserCalendar;

/// <summary>
/// Marks a calendar of the current user as the default one.
/// </summary>
public record SetDefaultUserCalendarCommand(Guid Id) : ITransactionalCommand<Response<UserCalendarDto>>;
