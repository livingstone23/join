using JOIN.Application.Common;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.DeleteCalendarEvent;

/// <summary>
/// Handles <see cref="DeleteCalendarEventCommand"/> through <see cref="CalendarEventCommandProcessor"/>.
/// </summary>
public sealed class DeleteCalendarEventCommandHandler(CalendarEventCommandProcessor processor)
    : IRequestHandler<DeleteCalendarEventCommand, Response<bool>>
{
    public Task<Response<bool>> Handle(DeleteCalendarEventCommand request, CancellationToken cancellationToken) =>
        processor.DeleteAsync(request.Id, request.Scope, request.OccurrenceStartUtc, panel: false, cancellationToken);
}
