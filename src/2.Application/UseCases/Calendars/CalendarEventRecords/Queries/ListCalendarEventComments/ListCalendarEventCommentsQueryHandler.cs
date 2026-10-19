using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.CalendarActivityIndex;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;

/// <summary>
/// SPEC 69 — handles <see cref="ListCalendarEventCommentsQuery"/>: validates the user can read the
/// activity (rule 5), returns the comments ordered by <c>CreatedUtc DESC</c> with their attached documents.
/// A deleted comment returns its metadata but <c>Body = null</c>.
/// </summary>
public sealed class ListCalendarEventCommentsQueryHandler(
    IUnitOfWork unitOfWork,
    CalendarActivityVisibility visibility)
    : IRequestHandler<ListCalendarEventCommentsQuery, Response<IReadOnlyList<CalendarEventCommentDto>>>
{
    public async Task<Response<IReadOnlyList<CalendarEventCommentDto>>> Handle(
        ListCalendarEventCommentsQuery request,
        CancellationToken cancellationToken)
    {
        var access = await visibility.ResolveEventAccessAsync(request.CalendarEventId, cancellationToken);
        if (!access.IsFoundAndVisible)
        {
            return Response<IReadOnlyList<CalendarEventCommentDto>>.Error(
                CalendarErrorCodes.EventNotFound,
                ["The calendar activity was not found or is not visible."]);
        }

        var calendarEvent = access.CalendarEvent!;
        var comments = (await unitOfWork.GetRepository<Domain.Calendars.CalendarEventComment>()
            .GetAllIncludingDeletedAsync(x => x.CalendarEventId == calendarEvent.Id && x.GcRecord == 0))
            .OrderByDescending(x => x.CreatedUtc)
            .ToList();

        var documents = (await unitOfWork.GetRepository<Domain.Calendars.CalendarEventDocument>()
            .GetAllIncludingDeletedAsync(x => x.CalendarEventId == calendarEvent.Id && x.GcRecord == 0))
            .GroupBy(d => d.CalendarEventCommentId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CalendarEventDocumentDto>)g.Select(MapDocument).ToList());

        var authorIds = comments.Select(x => x.AuthorUserId).Distinct().ToList();
        var authors = (await unitOfWork.GetRepository<Domain.Security.ApplicationUser>()
            .GetAllIncludingDeletedAsync(x => authorIds.Contains(x.Id)))
            .ToDictionary(x => x.Id, x => $"{x.FirstName} {x.LastName}".Trim());

        var items = comments.Select(c =>
        {
            var docs = documents.TryGetValue(c.Id, out var list) ? list : (IReadOnlyList<CalendarEventDocumentDto>)[];
            return new CalendarEventCommentDto
            {
                Id = c.Id,
                CalendarEventId = c.CalendarEventId,
                AuthorUserId = c.AuthorUserId,
                AuthorUserName = authors.TryGetValue(c.AuthorUserId, out var name) ? name : string.Empty,
                CreatedUtc = c.CreatedUtc,
                Body = c.DeletedUtc is null ? c.Body : null,
                Documents = docs,
                IsDeleted = c.DeletedUtc is not null,
                DeletedByUserId = c.DeletedByUserId,
                DeletedUtc = c.DeletedUtc,
                DeleteReason = c.DeleteReason
            };
        }).ToList();

        return new Response<IReadOnlyList<CalendarEventCommentDto>>
        {
            IsSuccess = true,
            Message = "Calendar comments retrieved successfully.",
            Data = items
        };
    }

    private static CalendarEventDocumentDto MapDocument(Domain.Calendars.CalendarEventDocument d) => new()
    {
        Id = d.Id,
        CalendarEventId = d.CalendarEventId,
        CalendarEventCommentId = d.CalendarEventCommentId,
        DocumentType = (int)d.DocumentType,
        OriginalName = d.OriginalName,
        NewName = d.NewName,
        ContentType = d.ContentType,
        SizeBytes = d.SizeBytes,
        UploadedByUserId = d.UploadedByUserId,
        UploadedByUserName = string.Empty,
        CreatedAt = d.Created,
        IsDeleted = d.DeletedUtc is not null
    };
}