using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityIndex.Queries;

/// <summary>
/// SPEC 68 — handles <see cref="GetCalendarActivityIndexEventByIdQuery"/> through
/// <see cref="CalendarEventQueryService"/>, threading the per-user "see others" flag into the
/// <c>ownOnly</c> argument so a user without the flag can never see someone else's detail (404).
/// </summary>
public sealed class GetCalendarActivityIndexEventByIdQueryHandler(
    CalendarEventQueryService queryService,
    CalendarActivityVisibility visibility)
    : IRequestHandler<GetCalendarActivityIndexEventByIdQuery, Response<CalendarEventDto>>
{
    public async Task<Response<CalendarEventDto>> Handle(GetCalendarActivityIndexEventByIdQuery request, CancellationToken cancellationToken)
    {
        var context = await visibility.ResolveAsync(cancellationToken);
        var ownOnly = !context.CanViewOthersActivities;
        return await queryService.GetByIdAsync(request.Id, ownOnly, cancellationToken);
    }
}