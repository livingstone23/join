using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;

/// <summary>
/// SPEC 69 — returns the documents of an activity, optionally filtered by comment. Every document of the
/// activity is returned when <paramref name="CalendarEventCommentId"/> is <c>null</c>; the soft-deleted
/// ones stay in the list with <c>IsDeleted</c> and the delete metadata (rule 4).
/// </summary>
public sealed record ListCalendarEventDocumentsQuery(Guid CalendarEventId, Guid? CalendarEventCommentId = null)
    : IRequest<Response<IReadOnlyList<CalendarEventDocumentDto>>>;