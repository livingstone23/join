using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.ChangeCalendarPanelEventStatus;

/// <summary>
/// Changes the status of another user's activity: needs <c>CalendarActivityConfirmation</c> (filter) and
/// <c>CalendarOthers.CanUpdate</c> (handler).
/// </summary>
public record ChangeCalendarPanelEventStatusCommand : ITransactionalCommand<Response<CalendarEventDto>>, ICalendarEventTarget
{
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public CalendarRecurrenceEditScope? Scope { get; init; }

    [JsonIgnore]
    public DateTime? OccurrenceStartUtc { get; init; }

    public Guid CalendarStatusCompanyId { get; init; }
    public string? Notes { get; init; }
}
