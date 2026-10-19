using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityIndex.Queries;

/// <summary>
/// SPEC 68 — returns the detail of one activity, honouring the per-user "see others" flag (rule 2/3):
/// a user without the flag sees only their own activities (another user's activity answers 404); a user with
/// the flag sees any activity of the company. Wraps <see cref="CalendarEventQueryService"/>; the read path
/// does not change.
/// </summary>
public sealed record GetCalendarActivityIndexEventByIdQuery(Guid Id)
    : IRequest<Response<CalendarEventDto>>;