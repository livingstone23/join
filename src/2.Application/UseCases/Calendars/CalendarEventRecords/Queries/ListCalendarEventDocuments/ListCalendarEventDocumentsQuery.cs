using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;

/// <summary>
/// SPEC 69 — returns the documents of an activity, optionally filtered by comment. Documents attached directly
/// to the activity are returned when <paramref name="CalendarEventCommentId"/> is <c>null</c>; the soft-deleted
/// ones are kept out of the list.
/// </summary>
public sealed record ListCalendarEventDocumentsQuery(Guid CalendarEventId, Guid? CalendarEventCommentId = null)
    : IRequest<Response<IReadOnlyList<CalendarEventDocumentDto>>>;