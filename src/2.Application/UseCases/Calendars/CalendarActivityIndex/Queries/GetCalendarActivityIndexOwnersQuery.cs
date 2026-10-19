using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityIndex.Queries;

/// <summary>
/// SPEC 68 — owners filter for the activity index (<c>GET /CalendarActivityIndex/owners</c>). With the
/// per-user "see others" flag off, the list is restricted to the current user; with the flag on, it returns
/// every member of the company that has a calendar. The optional <see cref="Search"/> matches against
/// first name, last name, and email (case-insensitive).
/// </summary>
public sealed record GetCalendarActivityIndexOwnersQuery(string? Search = null, int Limit = 50)
    : IRequest<Response<IReadOnlyList<CalendarActivityIndexOwnerItemDto>>>;