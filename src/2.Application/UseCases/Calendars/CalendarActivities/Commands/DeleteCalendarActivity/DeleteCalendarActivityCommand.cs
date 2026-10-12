using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.DeleteCalendarActivity;

/// <summary>
/// Logically deletes a global activity type. Blocked when a company has it enabled or it is a system code.
/// </summary>
public record DeleteCalendarActivityCommand(Guid Id) : ITransactionalCommand<Response<bool>>;
