using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.ChangeCalendarEventStatus;

/// <summary>
/// Handles <see cref="ChangeCalendarEventStatusCommand"/> through <see cref="CalendarEventCommandProcessor"/>.
/// </summary>
public sealed class ChangeCalendarEventStatusCommandHandler(CalendarEventCommandProcessor processor)
    : IRequestHandler<ChangeCalendarEventStatusCommand, Response<CalendarEventDto>>
{
    public Task<Response<CalendarEventDto>> Handle(ChangeCalendarEventStatusCommand request, CancellationToken cancellationToken) =>
        processor.ChangeStatusAsync(request.Id, request.Scope, request.OccurrenceStartUtc, request.CalendarStatusCompanyId, request.Notes, panel: false, cancellationToken);
}
