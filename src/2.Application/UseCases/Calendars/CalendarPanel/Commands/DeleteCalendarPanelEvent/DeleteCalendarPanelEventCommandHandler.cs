using JOIN.Application.Common;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.DeleteCalendarPanelEvent;

/// <summary>
/// Handles <see cref="DeleteCalendarPanelEventCommand"/> through <see cref="CalendarEventCommandProcessor"/>.
/// </summary>
public sealed class DeleteCalendarPanelEventCommandHandler(CalendarEventCommandProcessor processor)
    : IRequestHandler<DeleteCalendarPanelEventCommand, Response<bool>>
{
    public Task<Response<bool>> Handle(DeleteCalendarPanelEventCommand request, CancellationToken cancellationToken) =>
        processor.DeleteAsync(request.Id, request.Scope, request.OccurrenceStartUtc, panel: true, cancellationToken);
}
