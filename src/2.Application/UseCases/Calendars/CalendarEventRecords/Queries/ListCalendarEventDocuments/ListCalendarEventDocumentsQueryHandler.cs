using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.CalendarActivityIndex;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;

/// <summary>
/// SPEC 69 — handles <see cref="ListCalendarEventDocumentsQuery"/>: validates the user can read the activity
/// (rule 5), returns the live documents (deleted ones excluded) with the uploader full name.
/// </summary>
public sealed class ListCalendarEventDocumentsQueryHandler(
    IUnitOfWork unitOfWork,
    CalendarActivityVisibility visibility)
    : IRequestHandler<ListCalendarEventDocumentsQuery, Response<IReadOnlyList<CalendarEventDocumentDto>>>
{
    public async Task<Response<IReadOnlyList<CalendarEventDocumentDto>>> Handle(
        ListCalendarEventDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        var access = await visibility.ResolveEventAccessAsync(request.CalendarEventId, cancellationToken);
        if (!access.IsFoundAndVisible)
        {
            return Response<IReadOnlyList<CalendarEventDocumentDto>>.Error(
                CalendarErrorCodes.EventNotFound,
                ["The calendar activity was not found or is not visible."]);
        }

        var calendarEvent = access.CalendarEvent!;
        var documents = (await unitOfWork.GetRepository<Domain.Calendars.CalendarEventDocument>()
            .GetAllIncludingDeletedAsync(x => x.CalendarEventId == calendarEvent.Id
                && x.GcRecord == 0
                && (request.CalendarEventCommentId == null || x.CalendarEventCommentId == request.CalendarEventCommentId)))
            .OrderBy(x => x.Created)
            .ToList();

        var uploaderIds = documents.Select(x => x.UploadedByUserId).Distinct().ToList();
        var uploaders = (await unitOfWork.GetRepository<Domain.Security.ApplicationUser>()
            .GetAllIncludingDeletedAsync(x => uploaderIds.Contains(x.Id)))
            .ToDictionary(x => x.Id, x => $"{x.FirstName} {x.LastName}".Trim());

        var items = documents.Select(d => new CalendarEventDocumentDto
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
            UploadedByUserName = uploaders.TryGetValue(d.UploadedByUserId, out var name) ? name : string.Empty,
            CreatedAt = d.Created,
            IsDeleted = false
        }).ToList();

        return new Response<IReadOnlyList<CalendarEventDocumentDto>>
        {
            IsSuccess = true,
            Message = "Calendar documents retrieved successfully.",
            Data = items
        };
    }
}