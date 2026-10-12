using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.ChangeCalendarEventStatus;

/// <summary>
/// Changes the status of an activity of the current user (<c>CalendarActivityConfirmation</c>). Cancelling requires
/// <c>notes</c>.
/// </summary>
public record ChangeCalendarEventStatusCommand : ITransactionalCommand<Response<CalendarEventDto>>, ICalendarEventTarget
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
