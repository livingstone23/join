using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// <c>POST /events/{id}/cancel</c>: cancels an activity of the requester (SPEC 45 F4). Validates ownership
/// (<see cref="JOIN.Domain.Calendars.CalendarEvent.RequesterPersonId"/> must match), refuses to reopen
/// final-status activities, and writes the reason as the log's <c>Notes</c>.
/// </summary>
public record CancelIntakeEventCommand(
    Guid CompanyId,
    Guid EventId,
    Guid RequesterPersonId,
    string Reason,
    CalendarRecurrenceEditScope? Scope,
    DateTime? OccurrenceStartUtc,
    string OriginChannelCode,
    string OriginIdentifier)
    : IRequest<Response<CancelIntakeEventDto>>, ITransactionalCommand<CancelIntakeEventDto>;