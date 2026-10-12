using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Commands.CreateUserCalendar;

/// <summary>
/// Creates another calendar of the current user in the company.
/// </summary>
public record CreateUserCalendarCommand : ITransactionalCommand<Response<UserCalendarDto>>
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Color { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}
