using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;

/// <summary>
/// SPEC 69 — handles <see cref="ListCalendarEventDocumentsQuery"/>: validates the user can read the activity
/// (rule 5) and reads the documents with Dapper, oldest first, with the uploader full name. Soft-deleted ones
/// are tagged <c>IsDeleted</c>.
/// </summary>
public sealed class ListCalendarEventDocumentsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    CalendarEventRecordsAccess access)
    : IRequestHandler<ListCalendarEventDocumentsQuery, Response<IReadOnlyList<CalendarEventDocumentDto>>>
{
    public async Task<Response<IReadOnlyList<CalendarEventDocumentDto>>> Handle(
        ListCalendarEventDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        var eventAccess = await access.ResolveAsync(request.CalendarEventId, cancellationToken);
        if (!eventAccess.IsFoundAndVisible)
        {
            return Response<IReadOnlyList<CalendarEventDocumentDto>>.Error(
                CalendarErrorCodes.EventNotFound,
                ["The calendar activity was not found or is not visible."]);
        }

        var calendarEvent = eventAccess.CalendarEvent!;
        using var connection = connectionFactory.CreateConnection();
        var items = await CalendarEventDocumentsSql.ReadAsync(
            connection, calendarEvent.CompanyId, calendarEvent.Id, request.CalendarEventCommentId, cancellationToken);

        return new Response<IReadOnlyList<CalendarEventDocumentDto>>
        {
            IsSuccess = true,
            Message = "Calendar documents retrieved successfully.",
            Data = items
        };
    }
}
