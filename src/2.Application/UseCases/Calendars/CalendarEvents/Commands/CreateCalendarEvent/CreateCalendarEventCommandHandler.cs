using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.CreateCalendarEvent;

/// <summary>
/// Handles <see cref="CreateCalendarEventCommand"/> through <see cref="CalendarEventCommandProcessor"/>.
/// </summary>
public sealed class CreateCalendarEventCommandHandler(CalendarEventCommandProcessor processor)
    : IRequestHandler<CreateCalendarEventCommand, Response<CalendarEventDto>>
{
    public Task<Response<CalendarEventDto>> Handle(CreateCalendarEventCommand request, CancellationToken cancellationToken) =>
        processor.CreateAsync(request, null, cancellationToken);
}
