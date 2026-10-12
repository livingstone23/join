using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.CancelIntakeEvent;

/// <summary>
/// <c>POST /CalendarChannelIntake/events/{id}/cancel</c> (SPEC 45): moves an activity of the identified requester to
/// the company's <c>CANCELLED</c> status; <see cref="Reason"/> goes to the log notes. Without <see cref="Scope"/> a
/// series cancels only the occurrence (<see cref="OccurrenceStartUtc"/>). The company is the one of the token.
/// </summary>
public sealed record CancelIntakeEventCommand(
    Guid EventId,
    Guid RequesterPersonId,
    string Reason,
    CalendarRecurrenceEditScope? Scope,
    DateTime? OccurrenceStartUtc,
    string OriginChannelCode,
    string OriginIdentifier)
    : ITransactionalCommand<Response<CancelIntakeEventDto>>;
