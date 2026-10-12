using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.RescheduleIntakeEvent;

/// <summary>
/// <c>PUT /CalendarChannelIntake/events/{id}/reschedule</c> (SPEC 45): moves an activity of the identified requester.
/// Without <see cref="End"/> the activity keeps its duration; without <see cref="Scope"/> a series changes only the
/// occurrence (<see cref="OccurrenceStartUtc"/>). A repeated <see cref="OriginReference"/> answers with the current
/// state without applying again. The company is the one of the token.
/// </summary>
public sealed record RescheduleIntakeEventCommand(
    Guid EventId,
    Guid RequesterPersonId,
    DateTimeOffset Start,
    DateTimeOffset? End,
    CalendarRecurrenceEditScope? Scope,
    DateTime? OccurrenceStartUtc,
    string OriginChannelCode,
    string OriginIdentifier,
    string OriginReference,
    string? Notes)
    : ITransactionalCommand<Response<RescheduleIntakeEventDto>>;
