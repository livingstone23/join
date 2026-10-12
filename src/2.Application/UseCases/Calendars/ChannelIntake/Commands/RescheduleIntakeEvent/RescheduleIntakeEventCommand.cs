using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// <c>PUT /events/{id}/reschedule</c>: moves an existing activity of the requester (SPEC 45 F4). The
/// handler enforces requester ownership, refuses to reopen final-status activities, and is idempotent on
/// <c>OriginReference</c> (the same reference in the event's log answers without applying).
/// </summary>
public record RescheduleIntakeEventCommand(
    Guid CompanyId,
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
    : IRequest<Response<RescheduleIntakeEventDto>>, ITransactionalCommand<RescheduleIntakeEventDto>;