using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.CreateCalendarPanelEvent;

/// <summary>
/// Handles <see cref="CreateCalendarPanelEventCommand"/> through <see cref="CalendarEventCommandProcessor"/>.
/// </summary>
public sealed class CreateCalendarPanelEventCommandHandler(CalendarEventCommandProcessor processor)
    : IRequestHandler<CreateCalendarPanelEventCommand, Response<CalendarEventDto>>
{
    public Task<Response<CalendarEventDto>> Handle(CreateCalendarPanelEventCommand request, CancellationToken cancellationToken) =>
        processor.CreateAsync(request, request.OwnerUserId, cancellationToken);
}
