using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;

/// <summary>
/// SPEC 69 — handles <see cref="ListCalendarEventCommentsQuery"/>: validates the user can read the
/// activity (rule 5) and reads with Dapper the comments ordered by <c>CreatedUtc DESC</c>, each with its
/// attached documents. A deleted comment returns who deleted it, when and why, but <c>Body = null</c>.
/// </summary>
public sealed class ListCalendarEventCommentsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    CalendarEventRecordsAccess access)
    : IRequestHandler<ListCalendarEventCommentsQuery, Response<IReadOnlyList<CalendarEventCommentDto>>>
{
    private const string CommentsSql = """
        SELECT c.Id, c.CalendarEventId, c.AuthorUserId, CONCAT(a.FirstName, ' ', a.LastName) AS AuthorUserName,
               c.CreatedUtc, c.Body, c.DeletedByUserId, CONCAT(du.FirstName, ' ', du.LastName) AS DeletedByUserName,
               c.DeletedUtc, c.DeleteReason
        FROM Calendar.EventComments c
        LEFT JOIN Security.Users a ON a.Id = c.AuthorUserId
        LEFT JOIN Security.Users du ON du.Id = c.DeletedByUserId
        WHERE c.CompanyId = @TenantId
          AND c.GcRecord = 0
          AND c.CalendarEventId = @EventId
        ORDER BY c.CreatedUtc DESC;
        """;

    public async Task<Response<IReadOnlyList<CalendarEventCommentDto>>> Handle(
        ListCalendarEventCommentsQuery request,
        CancellationToken cancellationToken)
    {
        var eventAccess = await access.ResolveAsync(request.CalendarEventId, cancellationToken);
        if (!eventAccess.IsFoundAndVisible)
        {
            return Response<IReadOnlyList<CalendarEventCommentDto>>.Error(
                CalendarErrorCodes.EventNotFound,
                ["The calendar activity was not found or is not visible."]);
        }

        var calendarEvent = eventAccess.CalendarEvent!;
        using var connection = connectionFactory.CreateConnection();
        var comments = (await connection.QueryAsync<CommentRow>(new CommandDefinition(
                CommentsSql,
                new { TenantId = calendarEvent.CompanyId, EventId = calendarEvent.Id },
                cancellationToken: cancellationToken)))
            .ToList();

        var documentsByComment = (await CalendarEventDocumentsSql.ReadAsync(
                connection, calendarEvent.CompanyId, calendarEvent.Id, commentId: null, cancellationToken))
            .Where(d => d.CalendarEventCommentId is not null)
            .GroupBy(d => d.CalendarEventCommentId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CalendarEventDocumentDto>)g.ToList());

        var items = comments.Select(c => new CalendarEventCommentDto
        {
            Id = c.Id,
            CalendarEventId = c.CalendarEventId,
            AuthorUserId = c.AuthorUserId,
            AuthorUserName = c.AuthorUserName?.Trim() ?? string.Empty,
            CreatedUtc = c.CreatedUtc,
            Body = c.DeletedUtc is null ? c.Body : null,
            Documents = documentsByComment.TryGetValue(c.Id, out var docs) ? docs : [],
            IsDeleted = c.DeletedUtc is not null,
            DeletedByUserId = c.DeletedByUserId,
            DeletedByUserName = CalendarEventDocumentsSql.NullIfBlank(c.DeletedByUserName),
            DeletedUtc = c.DeletedUtc,
            DeleteReason = c.DeleteReason
        }).ToList();

        return new Response<IReadOnlyList<CalendarEventCommentDto>>
        {
            IsSuccess = true,
            Message = "Calendar comments retrieved successfully.",
            Data = items
        };
    }

    private sealed class CommentRow
    {
        public Guid Id { get; set; }
        public Guid CalendarEventId { get; set; }
        public Guid AuthorUserId { get; set; }
        public string? AuthorUserName { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string Body { get; set; } = string.Empty;
        public Guid? DeletedByUserId { get; set; }
        public string? DeletedByUserName { get; set; }
        public DateTime? DeletedUtc { get; set; }
        public string? DeleteReason { get; set; }
    }
}
