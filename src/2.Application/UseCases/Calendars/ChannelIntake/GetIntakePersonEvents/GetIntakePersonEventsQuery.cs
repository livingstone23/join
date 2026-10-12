using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.GetIntakePersonEvents;

/// <summary>
/// Non-cancelled activities of a person in the current company in a range (default <c>from</c> = today UTC, max
/// range 90 days; SPEC 45, <c>GET /events</c>). Used by the agent to reschedule or cancel an existing activity.
/// </summary>
public sealed record GetIntakePersonEventsQuery(
    Guid RequesterPersonId,
    DateTimeOffset? From,
    DateTimeOffset? To)
    : IRequest<Response<IReadOnlyList<IntakeEventDto>>>;