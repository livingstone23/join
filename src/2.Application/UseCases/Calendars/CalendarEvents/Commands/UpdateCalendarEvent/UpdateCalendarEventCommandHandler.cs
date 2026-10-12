using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.UpdateCalendarEvent;

/// <summary>
/// Handles <see cref="UpdateCalendarEventCommand"/> through <see cref="CalendarEventCommandProcessor"/>.
/// </summary>
public sealed class UpdateCalendarEventCommandHandler(CalendarEventCommandProcessor processor)
    : IRequestHandler<UpdateCalendarEventCommand, Response<CalendarEventDto>>
{
    public Task<Response<CalendarEventDto>> Handle(UpdateCalendarEventCommand request, CancellationToken cancellationToken) =>
        processor.UpdateAsync(request.Id, request.Scope, request.OccurrenceStartUtc, request, panel: false, cancellationToken);
}
