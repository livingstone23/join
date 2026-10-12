using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.ChangeCalendarPanelEventStatus;

/// <summary>
/// Handles <see cref="ChangeCalendarPanelEventStatusCommand"/>: checks <c>CalendarOthers.CanUpdate</c> first.
/// </summary>
public sealed class ChangeCalendarPanelEventStatusCommandHandler(CalendarEventCommandProcessor processor, CalendarPanelAccess access)
    : IRequestHandler<ChangeCalendarPanelEventStatusCommand, Response<CalendarEventDto>>
{
    public async Task<Response<CalendarEventDto>> Handle(ChangeCalendarPanelEventStatusCommand request, CancellationToken cancellationToken)
    {
        var allowed = await access.RequireOthersUpdateAsync();
        if (!allowed.IsSuccess)
        {
            return Response<CalendarEventDto>.Error(allowed.Message, allowed.Errors);
        }

        return await processor.ChangeStatusAsync(request.Id, request.Scope, request.OccurrenceStartUtc, request.CalendarStatusCompanyId, request.Notes, panel: true, cancellationToken);
    }
}
