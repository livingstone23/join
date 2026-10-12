using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.CreateCalendarActivity;

/// <summary>
/// Creates a global activity type (SPEC 44, <c>CalendarCatalogs</c>).
/// </summary>
public record CreateCalendarActivityCommand : ITransactionalCommand<Response<CalendarActivityDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Color { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public int DefaultDurationMinutes { get; init; }
    public bool AllowsOverlap { get; init; }
    public bool RequiresBusinessHours { get; init; }
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }
}
