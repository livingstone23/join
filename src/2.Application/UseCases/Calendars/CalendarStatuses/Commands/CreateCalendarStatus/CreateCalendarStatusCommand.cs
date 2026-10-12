using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.CreateCalendarStatus;

/// <summary>
/// Creates a global status (SPEC 44, <c>CalendarCatalogs</c>).
/// </summary>
public record CreateCalendarStatusCommand : ITransactionalCommand<Response<CalendarStatusDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Color { get; init; } = string.Empty;
    public bool BlocksTime { get; init; } = true;
    public bool IsFinal { get; init; }
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }
}
