using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.UpdateCalendarPanelEvent;

/// <summary>
/// Handles <see cref="UpdateCalendarPanelEventCommand"/> through <see cref="CalendarEventCommandProcessor"/>.
/// </summary>
public sealed class UpdateCalendarPanelEventCommandHandler(CalendarEventCommandProcessor processor)
    : IRequestHandler<UpdateCalendarPanelEventCommand, Response<CalendarEventDto>>
{
    public Task<Response<CalendarEventDto>> Handle(UpdateCalendarPanelEventCommand request, CancellationToken cancellationToken) =>
        processor.UpdateAsync(request.Id, request.Scope, request.OccurrenceStartUtc, request, panel: true, cancellationToken);
}
