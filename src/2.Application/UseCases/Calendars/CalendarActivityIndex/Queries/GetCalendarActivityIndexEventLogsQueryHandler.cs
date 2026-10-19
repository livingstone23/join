using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityIndex.Queries;

/// <summary>
/// SPEC 68 — handles <see cref="GetCalendarActivityIndexEventLogsQuery"/> through
/// <see cref="CalendarEventQueryService"/>, threading the per-user "see others" flag into the
/// <c>ownOnly</c> argument.
/// </summary>
public sealed class GetCalendarActivityIndexEventLogsQueryHandler(
    CalendarEventQueryService queryService,
    CalendarActivityVisibility visibility)
    : IRequestHandler<GetCalendarActivityIndexEventLogsQuery, Response<IReadOnlyList<CalendarEventLogDto>>>
{
    public async Task<Response<IReadOnlyList<CalendarEventLogDto>>> Handle(
        GetCalendarActivityIndexEventLogsQuery request,
        CancellationToken cancellationToken)
    {
        var context = await visibility.ResolveAsync(cancellationToken);
        var ownOnly = !context.CanViewOthersActivities;
        return await queryService.GetLogsAsync(request.Id, ownOnly, cancellationToken);
    }
}